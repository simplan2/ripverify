namespace RipVerify.Core.Models;

public sealed class AppSettings
{
    public const string DefaultMusicBrainzUserAgent = "RipVerify/1.0 (test@example.com)";

    public static bool IsValidMusicBrainzUserAgent(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains("RipVerify/", StringComparison.Ordinal)
            && !value.Contains('<', StringComparison.Ordinal)
            && !value.Contains('>', StringComparison.Ordinal);
    }

    public string CueToolsPath { get; set; } = string.Empty;
    public string FfmpegPath { get; set; } = string.Empty;
    public int DriveOffset { get; set; }
    public string DefaultLanguage { get; set; } = "en";
    public string LogLevel { get; set; } = "Information";
    public string MusicBrainzUserAgent { get; set; } = DefaultMusicBrainzUserAgent;
    public bool FirstRunCompleted { get; set; }
}
