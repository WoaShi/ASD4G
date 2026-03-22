namespace ASD4G.Models;

public sealed class TargetProgramSetting
{
    public string ExecutablePath { get; set; } = string.Empty;

    public int TargetWidth { get; set; } = 2280;

    public int TargetHeight { get; set; } = 1280;
}
