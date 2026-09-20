using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Threading;

namespace mdaiAgent;

public class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    public static LocalizationManager Instance => _instance.Value;

    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public string CurrentLanguageCode => _currentCulture.TwoLetterISOLanguageName;

    public bool IsEnglish => string.Equals(CurrentLanguageCode, "en", StringComparison.OrdinalIgnoreCase);
    public bool IsTurkish => string.Equals(CurrentLanguageCode, "tr", StringComparison.OrdinalIgnoreCase);
    public bool IsChinese => string.Equals(CurrentLanguageCode, "zh", StringComparison.OrdinalIgnoreCase);

    private LocalizationManager()
    {
        _resourceManager = new ResourceManager(
            "mdaiAgent.Resources.Strings",
            typeof(LocalizationManager).Assembly);

        _currentCulture = CultureInfo.CurrentUICulture;
    }

    public void Initialize(string? languageCode = null)
    {
        SetLanguage(languageCode ?? DetectSystemLanguage());
    }

    public string this[string key] => GetString(key);

    public string GetString(string key, params object[] args)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        string? value = null;
        try
        {
            value = _resourceManager.GetString(key, _currentCulture);
        }
        catch (MissingManifestResourceException)
        {
            value = null;
        }
        catch (Exception)
        {
            value = null;
        }

        if (string.IsNullOrEmpty(value))
        {
            try
            {
                value = _resourceManager.GetString(key, CultureInfo.GetCultureInfo("tr-TR"));
            }
            catch
            {
                value = $"[{key}]";
            }
        }

        if (args.Length > 0 && !string.IsNullOrEmpty(value))
        {
            try
            {
                value = string.Format(_currentCulture, value, args);
            }
            catch
            {
            }
        }

        return value ?? $"[{key}]";
    }

    public string GetStringOrFallback(string key, string fallback)
    {
        var value = GetString(key);
        return value == $"[{key}]" ? fallback : value;
    }

    public T Get<T>(string key)
    {
        var value = _resourceManager.GetObject(key, _currentCulture);
        if (value is T typed)
            return typed;
        return default!;
    }

    public void SetLanguage(string? languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        var code = normalized switch
        {
            "en" => "en-US",
            "zh" => "zh-CN",
            _ => "tr-TR"
        };
        var culture = new CultureInfo(code);

        if (_currentCulture.Name != culture.Name)
        {
            _currentCulture = culture;

            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            OnPropertyChanged(string.Empty);
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string NormalizeLanguageCode(string? languageCode)
    {
        if (string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase))
            return "en";
        if (string.Equals(languageCode, "zh", StringComparison.OrdinalIgnoreCase))
            return "zh";
        return "tr";
    }

    public static string DetectSystemLanguage()
    {
        var systemCulture = CultureInfo.CurrentUICulture.Name;
        if (systemCulture.StartsWith("tr", StringComparison.OrdinalIgnoreCase))
            return "tr";
        if (systemCulture.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return "en";
        if (systemCulture.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh";
        return "tr";
    }

    public IEnumerable<string> GetAvailableKeys()
    {
        var keys = new HashSet<string>();
        try
        {
            var rs = _resourceManager.GetResourceSet(CultureInfo.GetCultureInfo("tr-TR"), true, true);
            if (rs != null)
            {
                foreach (System.Collections.DictionaryEntry entry in rs)
                {
                    if (entry.Key is string k)
                        keys.Add(k);
                }
            }
        }
        catch
        {
        }
        return keys.OrderBy(k => k);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
