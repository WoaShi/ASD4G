namespace ASD4G.Models;

public sealed class LanguageBundle
{
    public string LanguageCode { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public Dictionary<string, string> Strings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
