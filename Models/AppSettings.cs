namespace ASD4G.Models;

public sealed class AppSettings
{
    public bool AutoStart { get; set; }

    public bool MonitoringEnabled { get; set; } = true;

    public int TargetWidth { get; set; } = 2280;

    public int TargetHeight { get; set; } = 1280;

    public string SelectedLanguage { get; set; } = "zh-CN";

    public List<TargetProgramSetting> TargetProgramSettings { get; set; } = [];

    public List<string> TargetPrograms { get; set; } = [];
}
