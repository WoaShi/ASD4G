using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using ASD4G.Models;

namespace ASD4G.Services;

public sealed class ProcessMonitorService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private HashSet<string> _targetPaths = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ProcessMatchInfo> _activeProcesses = [];

    public ProcessMonitorService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += OnTick;
    }

    public event EventHandler? ActiveProcessesChanged;

    public IReadOnlyList<ProcessMatchInfo> ActiveProcesses => _activeProcesses;

    public void Start()
    {
        if (_timer.IsEnabled)
        {
            return;
        }

        _timer.Start();
        EvaluateActiveProcesses();
    }

    public void Stop()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
        }

        UpdateActiveProcesses([]);
    }

    public void UpdateTargets(IEnumerable<string> executablePaths)
    {
        _targetPaths = executablePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        EvaluateActiveProcesses();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        EvaluateActiveProcesses();
    }

    private void EvaluateActiveProcesses()
    {
        if (_targetPaths.Count == 0)
        {
            UpdateActiveProcesses([]);
            return;
        }

        var matches = new List<ProcessMatchInfo>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var executablePath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    continue;
                }

                executablePath = Path.GetFullPath(executablePath);
                if (!_targetPaths.Contains(executablePath))
                {
                    continue;
                }

                matches.Add(new ProcessMatchInfo
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    ExecutablePath = executablePath
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

        UpdateActiveProcesses(matches
            .OrderBy(match => match.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.ProcessId)
            .ToList());
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
}
