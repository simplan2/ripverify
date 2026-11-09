namespace RipVerify.App.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RipVerify.App.Models;
using RipVerify.App.Services;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using System.Collections.ObjectModel;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private readonly IFileValidator _fileValidator;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _cueToolsPath = string.Empty;

    [ObservableProperty]
    private string _ffmpegPath = string.Empty;

    [ObservableProperty]
    private int _driveOffset;

    [ObservableProperty]
    private string _defaultLanguage = "en";

    [ObservableProperty]
    private string _logLevel = "Information";

    [ObservableProperty]
    private string _musicBrainzUserAgent = string.Empty;

    [ObservableProperty]
    private string _validationError = string.Empty;

    [ObservableProperty]
    private bool _hasValidationError;

    [ObservableProperty]
    private string _cueToolsPathLabel = string.Empty;

    [ObservableProperty]
    private string _ffmpegPathLabel = string.Empty;

    [ObservableProperty]
    private string _driveOffsetLabel = string.Empty;

    [ObservableProperty]
    private string _languageLabel = string.Empty;

    [ObservableProperty]
    private string _logLevelLabel = string.Empty;

    [ObservableProperty]
    private string _musicBrainzUserAgentLabel = string.Empty;

    [ObservableProperty]
    private string _saveText = string.Empty;

    [ObservableProperty]
    private string _cancelText = string.Empty;

    [ObservableProperty]
    private string _englishText = string.Empty;

    [ObservableProperty]
    private string _spanishText = string.Empty;

    public ObservableCollection<string> LogLevels { get; } = new() { "Verbose", "Debug", "Information", "Warning", "Error" };

    public List<LanguageOption> Languagaes { get; } = new()
    {
        new LanguageOption { Code = "en", DisplayName = "English" },
        new LanguageOption { Code = "es", DisplayName = "Español" }
    };

    public LanguageOption SelectedLanguage
    {
        get => Languagaes.FirstOrDefault(x => x.Code == DefaultLanguage) ?? Languagaes.First();
        set
        {
            if (value is not null)
            {
                DefaultLanguage = value.Code;
                OnPropertyChanged();
            }
        }
    }

    public SettingsViewModel(ISettingsService settingsService, ILocalizationService localizationService, IFileValidator fileValidator)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        _fileValidator = fileValidator;

        Load();
        UpdateStrings();
        _localizationService.LanguageChanged += (_, _) => UpdateStrings();
    }

    private void Load()
    {
        var s = _settingsService.Current;
        CueToolsPath = s.CueToolsPath;
        FfmpegPath = s.FfmpegPath;
        DriveOffset = s.DriveOffset;
        DefaultLanguage = s.DefaultLanguage;
        LogLevel = s.LogLevel;
        MusicBrainzUserAgent = s.MusicBrainzUserAgent;
    }

    private void UpdateStrings()
    {
        Title = _localizationService.GetString("SettingsTitle");
        CueToolsPathLabel = _localizationService.GetString("SettingsCueToolsPath");
        FfmpegPathLabel = _localizationService.GetString("SettingsFfmpegPath");
        DriveOffsetLabel = _localizationService.GetString("SettingsDriveOffset");
        LanguageLabel = _localizationService.GetString("SettingsLanguage");
        LogLevelLabel = _localizationService.GetString("SettingsLogLevel");
        MusicBrainzUserAgentLabel = _localizationService.GetString("SettingsMusicBrainzUserAgent");
        SaveText = _localizationService.GetString("SettingsSave");
        CancelText = _localizationService.GetString("SettingsCancel");
        EnglishText = _localizationService.GetString("CommonEnglish");
        SpanishText = _localizationService.GetString("CommonSpanish");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var userAgent = MusicBrainzUserAgent?.Trim() ?? string.Empty;
        if (userAgent.Contains('<', StringComparison.Ordinal) || userAgent.Contains('>', StringComparison.Ordinal))
        {
            ValidationError = _localizationService.GetString("SettingsInvalidUserAgent");
            HasValidationError = true;
            return;
        }

        if (!AppSettings.IsValidMusicBrainzUserAgent(userAgent))
        {
            userAgent = AppSettings.DefaultMusicBrainzUserAgent;
        }

        var settings = new AppSettings
        {
            CueToolsPath = CueToolsPath?.Trim() ?? string.Empty,
            FfmpegPath = FfmpegPath?.Trim() ?? string.Empty,
            DriveOffset = DriveOffset,
            DefaultLanguage = DefaultLanguage,
            LogLevel = LogLevel,
            MusicBrainzUserAgent = userAgent,
            FirstRunCompleted = _settingsService.Current.FirstRunCompleted
        };

        ValidationError = string.Empty;
        HasValidationError = false;
        // --- GUARDAR ---
        await _settingsService.SaveAsync(settings);
        _localizationService.SetLanguage(settings.DefaultLanguage);
      
        // --- CIERRE ---
        // Buscamos de forma segura la ventana actual de la aplicación
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Si se abre como una ventana secundaria, buscar la ventana activa:
            var settingsWindow = desktop.Windows.FirstOrDefault(w => w.DataContext == this);
            if (settingsWindow != null)
            {
                settingsWindow.Close();
            }
        }
    }

    [RelayCommand]
    private async Task SetCueToolsPath()
    {
        var path = await PromptUserForExecutableAsync();
        if (path is not null) CueToolsPath = path;
    }

    [RelayCommand]
    private async Task SetFfmpegPath()
    {
        var path = await PromptUserForExecutableAsync();
        if (path is not null) FfmpegPath = path;
    }

    //[RelayCommand]
    //private void Cancel()
    //{
    //    Load();
    //}

    [RelayCommand]
    private void Reset()
    {
        Load();
    }

    partial void OnDefaultLanguageChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedLanguage));
    }

    // Método genérico y reutilizable para abrir el diálogo
    private async Task<string?> PromptUserForExecutableAsync()
    {
        var topLevel = TopLevel.GetTopLevel(
            Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null);

        if (topLevel is null) return null;

        var windowTitle = _localizationService.GetString("SelectExecutable") ?? "Select Executable";
        var fileterName = _localizationService.GetString("ExecutableFiles") ?? "Executable Files";
         
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = windowTitle,
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new($"{fileterName} (.exe)") // Corregido el typo "Ejectutables"
                {
                    Patterns = new List<string> { "*.exe" }
                }
            }
        });

        if (files.Count > 0 && files[0] is not null)
        {
            return files[0].Path.LocalPath;
        }

        return null;
    }
}
