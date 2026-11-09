namespace RipVerify.Core.Services;

using System.Text.Json;
using Microsoft.Extensions.Logging;
using RipVerify.Core.Models;

public class SettingsService : ISettingsService
{
    private readonly ILogger<SettingsService> _logger;
    private static readonly Action<ILogger, string, Exception?> _logSettingsCorrupted = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(1, nameof(LoadAsync)),
        "Settings file is corrupted. Using defaults. Path: {Path}");
    private static readonly Action<ILogger, Exception?> _logLoadFailed = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(2, nameof(LoadAsync)),
        "Failed to load settings. Using defaults.");
    private static readonly Action<ILogger, Exception?> _logSaveFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(3, nameof(SaveAsync)),
        "Failed to save settings.");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
    }

    public virtual string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RipVerify",
        "settings.json");

    public AppSettings Current { get; private set; } = new();

    public AppSettings GetDefaults()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return new AppSettings
        {
            CueToolsPath = string.Empty,
            FfmpegPath = string.Empty,
            DriveOffset = 0,
            DefaultLanguage = culture == "es" ? "es" : "en",
            LogLevel = "Information",
            MusicBrainzUserAgent = AppSettings.DefaultMusicBrainzUserAgent,
            FirstRunCompleted = false
        };
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(SettingsFilePath))
            {
                Current = GetDefaults();
                await SaveAsync(Current, cancellationToken).ConfigureAwait(false);
                return Current;
            }

            await using var stream = File.OpenRead(SettingsFilePath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (settings is null)
            {
                _logSettingsCorrupted(_logger, SettingsFilePath, null);
                Current = GetDefaults();
                await SaveAsync(Current, cancellationToken).ConfigureAwait(false);
                return Current;
            }

            if (!AppSettings.IsValidMusicBrainzUserAgent(settings.MusicBrainzUserAgent))
            {
                settings.MusicBrainzUserAgent = AppSettings.DefaultMusicBrainzUserAgent;
            }

            if (settings.DriveOffset < 0)
            {
                settings.DriveOffset = 0;
            }

            if (settings.DefaultLanguage != "en" && settings.DefaultLanguage != "es")
            {
                settings.DefaultLanguage = GetDefaults().DefaultLanguage;
            }

            if (settings.LogLevel != "Verbose" && settings.LogLevel != "Debug" && settings.LogLevel != "Information" && settings.LogLevel != "Warning" && settings.LogLevel != "Error")
            {
                settings.LogLevel = "Information";
            }

            Current = settings;
            return Current;
        }
        catch (Exception ex)
        {
            _logLoadFailed(_logger, ex);
            Current = GetDefaults();
            return Current;
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!AppSettings.IsValidMusicBrainzUserAgent(settings.MusicBrainzUserAgent))
            {
                settings.MusicBrainzUserAgent = AppSettings.DefaultMusicBrainzUserAgent;
            }

            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(SettingsFilePath);
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
            Current = settings;
        }
        catch (Exception ex)
        {
            _logSaveFailed(_logger, ex);
            throw;
        }
    }
}
