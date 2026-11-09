namespace RipVerify.App.Services;

using System.Globalization;
using System.Resources;
using System.Reflection;
using RipVerify.Core.Services;

public sealed class LocalizationService : ILocalizationService
{
    private readonly ResourceManager _resourceManager;
    private CultureInfo _culture;

    public LocalizationService()
    {
        _resourceManager = new ResourceManager("RipVerify.App.Resources.Strings", typeof(LocalizationService).Assembly);
        _culture = CultureInfo.CurrentUICulture;
        if (_culture.TwoLetterISOLanguageName != "es" && _culture.TwoLetterISOLanguageName != "en")
        {
            _culture = CultureInfo.GetCultureInfo("en");
        }
    }

    public string CurrentLanguage => _culture.TwoLetterISOLanguageName;
    public event EventHandler? LanguageChanged;

    public void SetLanguage(string language)
    {
        if (language != "en" && language != "es")
        {
            language = "en";
        }

        _culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentUICulture = _culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetString(string key)
    {
        var value = _resourceManager.GetString(key, _culture);
        return value ?? key;
    }

    public string GetString(string key, string language)
    {
        var culture = string.Equals(language, "es", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("es")
            : CultureInfo.GetCultureInfo("en");
        var value = _resourceManager.GetString(key, culture);
        return value ?? key;
    }
}
