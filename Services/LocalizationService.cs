using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using ASD4G.Infrastructure;
using ASD4G.Models;

namespace ASD4G.Services;

public sealed class LocalizationService : ObservableObject
{
    public static LocalizationService Instance { get; } = new();

    private readonly Dictionary<string, LanguageBundle> _bundles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<LanguageOption> _availableLanguages = [];
    private readonly ReadOnlyObservableCollection<LanguageOption> _readOnlyLanguages;
    private IReadOnlyDictionary<string, string> _currentStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private LanguageBundle? _fallbackBundle;
    private string _currentLanguageCode = "zh-CN";

    private LocalizationService()
    {
        _readOnlyLanguages = new ReadOnlyObservableCollection<LanguageOption>(_availableLanguages);
    }

    public event EventHandler? LanguageChanged;

    public ReadOnlyObservableCollection<LanguageOption> AvailableLanguages => _readOnlyLanguages;

    public string CurrentLanguageCode => _currentLanguageCode;

    public string this[string key] => Translate(key);

    public void Initialize(string languageDirectory, string preferredLanguageCode)
    {
        _bundles.Clear();
        _availableLanguages.Clear();

        if (Directory.Exists(languageDirectory))
        {
            foreach (var filePath in Directory.GetFiles(languageDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    var bundle = JsonSerializer.Deserialize<LanguageBundle>(json);
                    if (bundle is null ||
                        string.IsNullOrWhiteSpace(bundle.LanguageCode) ||
                        string.IsNullOrWhiteSpace(bundle.DisplayName) ||
                        bundle.Strings.Count == 0)
                    {
                        continue;
                    }

                    _bundles[bundle.LanguageCode] = bundle;
                }
                catch
                {
                }
            }
        }

        foreach (var bundle in _bundles.Values.OrderBy(bundle => bundle.DisplayName, StringComparer.CurrentCulture))
        {
            _availableLanguages.Add(new LanguageOption
            {
                Code = bundle.LanguageCode,
                DisplayName = bundle.DisplayName
            });
        }

        _fallbackBundle = _bundles.Values.FirstOrDefault(bundle => string.Equals(bundle.LanguageCode, "zh-CN", StringComparison.OrdinalIgnoreCase))
            ?? _bundles.Values.FirstOrDefault();

        SetLanguage(preferredLanguageCode);
    }

    public bool SetLanguage(string? languageCode)
    {
        LanguageBundle? bundle = null;

        if (!string.IsNullOrWhiteSpace(languageCode))
        {
            _bundles.TryGetValue(languageCode, out bundle);
        }

        bundle ??= _fallbackBundle;
        if (bundle is null)
        {
            return false;
        }

        _currentLanguageCode = bundle.LanguageCode;
        _currentStrings = bundle.Strings;

        OnPropertyChanged(nameof(CurrentLanguageCode));
        OnPropertyChanged("Item[]");
        LanguageChanged?.Invoke(this, EventArgs.Empty);

        return true;
    }

    public string Translate(string key)
    {
        if (_currentStrings.TryGetValue(key, out var localizedText))
        {
            return localizedText;
        }

        if (_fallbackBundle?.Strings.TryGetValue(key, out var fallbackText) == true)
        {
            return fallbackText;
        }

        return key;
    }

    public string TranslateFormat(string key, params object[] args)
    {
        return string.Format(Translate(key), args);
    }
}
