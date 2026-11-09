namespace RipVerify.Core.Models;

public sealed record CalibrationEntry
{
    public const string CdOrigin = "cd";
    public const string CdNaturalRolloffOrigin = "cd-natural-rolloff";
    public const string StreamingOrigin = "streaming";
    public const string VinylOrigin = "vinyl";
    public const string TranscodeOrigin = "transcode";
    public const string UnknownOrigin = "unknown";

    public static readonly IReadOnlySet<string> AllowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        CdOrigin,
        CdNaturalRolloffOrigin,
        StreamingOrigin,
        VinylOrigin,
        TranscodeOrigin,
        UnknownOrigin
    };

    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// Expected origin category. Allowed values:
    /// "cd"                 -> physical CD verified in AccurateRip, full spectrum.
    /// "cd-natural-rolloff" -> physical CD with mastering filter (gradual rolloff,
    ///                         no vertical brickwall). Must NOT be flagged as transcode.
    /// "streaming"          -> lossless from platform (Qobuz/Tidal), full spectrum.
    /// "vinyl"              -> vinyl rip, natural gradual rolloff.
    /// "transcode"          -> lossy source (MP3/AAC) re-encoded to FLAC, vertical brickwall.
    /// "unknown"            -> not tagged; excluded from calibration.
    /// </summary>
    public string ExpectedOrigin { get; init; } = "unknown";

    public double ObservedBrickwallScore { get; init; }
    public double ObservedNyquistCutoffHz { get; init; }
    public double ObservedRolloffSlopeDbPerKHz { get; init; }
    public double ObservedSpectralFlatness { get; init; }
    public string Notes { get; init; } = string.Empty;
    public DateTimeOffset TaggedAt { get; init; }
}
