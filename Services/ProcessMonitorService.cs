using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ASD4G.Models;

namespace ASD4G.Services;

public sealed class ProcessMonitorService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _evaluationGate = new(1, 1);
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _monitoringCancellationTokenSource;
    private Task? _monitoringTask;
    private IReadOnlyList<MonitoredTarget> _targets = [];
    private IReadOnlyList<ProcessMatchInfo> _activeProcesses = [];

    public event EventHandler? ActiveProcessesChanged;

    public IReadOnlyList<ProcessMatchInfo> ActiveProcesses => _activeProcesses;

    public void Start()
    {
        lock (_syncRoot)
        {
            if (_monitoringTask is not null)
            {
                return;
            }

            _monitoringCancellationTokenSource = new CancellationTokenSource();
            _monitoringTask = MonitorLoopAsync(_monitoringCancellationTokenSource.Token);
        }

        QueueEvaluation();
    }

    public void Stop()
    {
        CancellationTokenSource? cancellationTokenSource;

        lock (_syncRoot)
        {
            cancellationTokenSource = _monitoringCancellationTokenSource;
            _monitoringCancellationTokenSource = null;
            _monitoringTask = null;
        }

        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        UpdateActiveProcesses([]);
    }

    public void UpdateTargets(IEnumerable<TargetProgramSetting> targetPrograms)
    {
        lock (_syncRoot)
        {
            _targets = BuildTargets(targetPrograms);
        }

        QueueEvaluation();
    }

    public void Dispose()
    {
        Stop();
        _evaluationGate.Dispose();
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await EvaluateActiveProcessesAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void QueueEvaluation()
    {
        _ = EvaluateActiveProcessesAsync();
    }

    private async Task EvaluateActiveProcessesAsync()
    {
        if (!await _evaluationGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            IReadOnlyList<MonitoredTarget> targets;
            lock (_syncRoot)
            {
                targets = _targets;
            }

            if (targets.Count == 0)
            {
                UpdateActiveProcesses([]);
                return;
            }

            var matches = await Task.Run(() => FindMatches(targets)).ConfigureAwait(false);
            UpdateActiveProcesses(matches);
        }
        finally
        {
            _evaluationGate.Release();
        }
    }

    private static IReadOnlyList<MonitoredTarget> BuildTargets(IEnumerable<TargetProgramSetting> targetPrograms)
    {
        var targets = new List<MonitoredTarget>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (targetProgram, index) in targetPrograms.Select((value, index) => (value, index)))
        {
            if (string.IsNullOrWhiteSpace(targetProgram.ExecutablePath) ||
                !File.Exists(targetProgram.ExecutablePath))
            {
                continue;
            }

            var normalizedPath = Path.GetFullPath(targetProgram.ExecutablePath);
            if (!seenPaths.Add(normalizedPath))
            {
                continue;
            }

            targets.Add(new MonitoredTarget
            {
                ExecutablePath = normalizedPath,
                TargetWidth = Math.Max(1, targetProgram.TargetWidth),
                TargetHeight = Math.Max(1, targetProgram.TargetHeight),
                Priority = index,
                NormalizedAliases = BuildAliases(normalizedPath)
            });
        }

        return targets;
    }

    private static IReadOnlyList<ProcessMatchInfo> FindMatches(IReadOnlyList<MonitoredTarget> targets)
    {
        var matches = new List<ProcessMatchInfo>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var executablePath = TryGetExecutablePath(process);
                var processName = process.ProcessName;
                var matchedTarget = FindMatchedTarget(targets, processName, executablePath);
                if (matchedTarget is null)
                {
                    continue;
                }

                matches.Add(new ProcessMatchInfo
                {
                    ProcessId = process.Id,
                    ProcessName = processName,
                    ExecutablePath = executablePath ?? process.ProcessName,
                    TargetWidth = matchedTarget.TargetWidth,
                    TargetHeight = matchedTarget.TargetHeight,
                    TargetPriority = matchedTarget.Priority,
                    MatchedTargetPath = matchedTarget.ExecutablePath
                });
            }
            catch (Win32Exception)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (SystemException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return matches
            .OrderBy(match => match.TargetPriority)
            .ThenBy(match => match.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.ProcessId)
            .ToList();
    }

    private static MonitoredTarget? FindMatchedTarget(IReadOnlyList<MonitoredTarget> targets, string processName, string? executablePath)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var normalizedPath = Path.GetFullPath(executablePath);
            var pathMatch = targets.FirstOrDefault(target =>
                string.Equals(target.ExecutablePath, normalizedPath, StringComparison.OrdinalIgnoreCase));
            if (pathMatch is not null)
            {
                return pathMatch;
            }

            var executableFileName = NormalizeName(Path.GetFileNameWithoutExtension(normalizedPath));
            if (!string.IsNullOrWhiteSpace(executableFileName))
            {
                var fileNameMatch = targets.FirstOrDefault(target => target.NormalizedAliases.Contains(executableFileName));
                if (fileNameMatch is not null)
                {
                    return fileNameMatch;
                }
            }
        }

        var normalizedProcessName = NormalizeName(processName);
        return string.IsNullOrWhiteSpace(normalizedProcessName)
            ? null
            : targets.FirstOrDefault(target => target.NormalizedAliases.Contains(normalizedProcessName));
    }

    private static HashSet<string> BuildAliases(string executablePath)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddAlias(aliases, Path.GetFileNameWithoutExtension(executablePath));

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            AddAlias(aliases, versionInfo.InternalName);
            AddAlias(aliases, versionInfo.OriginalFilename);
            AddAlias(aliases, versionInfo.ProductName);
            AddAlias(aliases, versionInfo.FileDescription);
        }
        catch
        {
        }

        return aliases;
    }

    private static void AddAlias(ISet<string> aliases, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalizedValue = NormalizeName(Path.GetFileNameWithoutExtension(value.Trim()));
        if (!string.IsNullOrWhiteSpace(normalizedValue))
        {
            aliases.Add(normalizedValue);
        }
    }

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var characters = value.Where(char.IsLetterOrDigit).ToArray();
        return new string(characters).ToUpperInvariant();
    }

    private static string? TryGetExecutablePath(Process process)
    {
        var processHandle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
        if (processHandle != IntPtr.Zero)
        {
            try
            {
                var capacity = 1024;
                var builder = new StringBuilder(capacity);
                if (QueryFullProcessImageName(processHandle, 0, builder, ref capacity))
                {
                    return builder.ToString();
                }
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }

        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateActiveProcesses(IReadOnlyList<ProcessMatchInfo> newSnapshot)
    {
        if (SnapshotsEqual(_activeProcesses, newSnapshot))
        {
            return;
        }

        _activeProcesses = newSnapshot;
        ActiveProcessesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool SnapshotsEqual(IReadOnlyList<ProcessMatchInfo> left, IReadOnlyList<ProcessMatchInfo> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].ProcessId != right[index].ProcessId ||
                !string.Equals(left[index].ExecutablePath, right[index].ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class MonitoredTarget
    {
        public required string ExecutablePath { get; init; }

        public required int TargetWidth { get; init; }

        public required int TargetHeight { get; init; }

        public required int Priority { get; init; }

        public required HashSet<string> NormalizedAliases { get; init; }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(
        IntPtr processHandle,
        int flags,
        StringBuilder executablePath,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
