namespace RipVerify.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RipVerify.Core.Models;
using RipVerify.Core.Services;

public partial class FirstRunWizardViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private readonly IFileValidator _fileValidator;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _welcomeText = string.Empty;

    [ObservableProperty]
    private string _dependenciesText = string.Empty;

    [ObservableProperty]
    private string _cueToolsNotFoundText = string.Empty;

    [ObservableProperty]
    private string _downloadCueToolsText = string.Empty;

    [ObservableProperty]
    private bool _cueToolsFound;

    [ObservableProperty]
    private bool _acceptTerms;

    [ObservableProperty]
    private string _acceptTermsText = string.Empty;

    [ObservableProperty]
    private string _continueDegradedText = string.Empty;

    [ObservableProperty]
    private bool _continueDegraded;

    [ObservableProperty]
    private string _ffmpegInfoText = string.Empty;

    [ObservableProperty]
    private bool _ffmpegFound;

    [ObservableProperty]
    private string _languageText = string.Empty;

    [ObservableProperty]
    private string _selectedLanguage = "en";

    [ObservableProperty]
    private string _englishText = string.Empty;

    [ObservableProperty]
    private string _spanishText = string.Empty;

    [ObservableProperty]
    private string _nextText = string.Empty;

    [ObservableProperty]
    private string _backText = string.Empty;

    [ObservableProperty]
    private string _finishText = string.Empty;

    [ObservableProperty]
    private int _currentStep;

    public FirstRunWizardViewModel(ISettingsService settingsService, ILocalizationService localizationService, IFileValidator fileValidator)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        _fileValidator = fileValidator;

        CheckDependencies();
        UpdateStrings();
        _localizationService.LanguageChanged += (_, _) => UpdateStrings();
        CurrentStep = 0;
        SelectedLanguage = _localizationService.CurrentLanguage;
    }

    private void CheckDependencies()
    {
        var settings = _settingsService.Current;
        CueToolsFound = _fileValidator.IsCueToolsAvailable(settings.CueToolsPath);
        FfmpegFound = _fileValidator.IsFfmpegAvailable(settings.FfmpegPath);
    }

    private void UpdateStrings()
    {
        Title = _localizationService.GetString("FirstRunTitle");
        WelcomeText = _localizationService.GetString("FirstRunWelcome");
        DependenciesText = _localizationService.GetString("FirstRunDependencies");
        CueToolsNotFoundText = _localizationService.GetString("FirstRunCueToolsNotFound");
        DownloadCueToolsText = _localizationService.GetString("FirstRunDownloadCueTools");
        AcceptTermsText = _localizationService.GetString("FirstRunAcceptTerms");
        ContinueDegradedText = _localizationService.GetString("FirstRunContinueDegraded");
        FfmpegInfoText = _localizationService.GetString("FirstRunFfmpegInfo");
        LanguageText = _localizationService.GetString("FirstRunLanguage");
        EnglishText = _localizationService.GetString("CommonEnglish");
        SpanishText = _localizationService.GetString("CommonSpanish");
        NextText = _localizationService.GetString("FirstRunNext");
        BackText = _localizationService.GetString("FirstRunBack");
        FinishText = _localizationService.GetString("FirstRunFinish");
    }

    [RelayCommand]
    private void Next()
    {
        if (CurrentStep < 3)
        {
            CurrentStep++;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        _localizationService.SetLanguage(SelectedLanguage);

        var settings = new AppSettings
        {
            CueToolsPath = _settingsService.Current.CueToolsPath,
            FfmpegPath = _settingsService.Current.FfmpegPath,
            DriveOffset = _settingsService.Current.DriveOffset,
            DefaultLanguage = SelectedLanguage == "es" ? "es" : "en",
            LogLevel = _settingsService.Current.LogLevel,
            MusicBrainzUserAgent = _settingsService.Current.MusicBrainzUserAgent,
            FirstRunCompleted = true
        };

        await _settingsService.SaveAsync(settings);
    }
}
