namespace RipVerify.Core.Services;

public interface ILocalizationService
{
    string CurrentLanguage { get; }
    event EventHandler? LanguageChanged;
    void SetLanguage(string language);
    string GetString(string key);
    string GetString(string key, string language);
}
