namespace ASD4G.Models;

public enum DisplayOperationFailure
{
    None,
    CurrentModeUnavailable,
    TargetModeUnsupported,
    ApplyFailed,
    RestoreFailed
}

public sealed class DisplayOperationResult
{
    public bool Success { get; init; }

    public DisplayOperationFailure Failure { get; init; }

    public DisplayMode? Mode { get; init; }
}
