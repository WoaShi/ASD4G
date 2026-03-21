namespace ASD4G.Models;

public sealed class ProcessMatchInfo
{
    public required int ProcessId { get; init; }

    public required string ProcessName { get; init; }

    public required string ExecutablePath { get; init; }
}
