namespace RipVerify.Core.Models;

public sealed record ReportDocument
{
    public int SchemaVersion { get; init; } = 3;
    public string App { get; init; } = "RipVerify";
    public string AppVersion { get; init; } = string.Empty;
    public DateTimeOffset VerifiedAt { get; init; }
    public string AlbumPath { get; init; } = string.Empty;
    public AlbumMetadata Album { get; init; } = new();
    public IReadOnlyList<ReportResultEntry> Results { get; init; } = Array.Empty<ReportResultEntry>();
}

public sealed record ReportResultEntry
{
    public string AlbumPath { get; init; } = string.Empty;
    public string? CuePath { get; init; }
    public CueOrigin CueOrigin { get; init; } = CueOrigin.Original;
    public DbPowerampEvidence DbPoweramp { get; init; } = DbPowerampEvidence.Empty;
    public AccurateRipStatus AccurateRipStatus { get; init; }
    public int ConfidenceScore { get; init; }
    public CtdbStatus CtdbStatus { get; init; }
    public int CtdbConfidenceScore { get; init; }
    public int? DriveOffset { get; init; }
    public bool CtdbParityAvailable { get; init; }
    public SpectrumAnalysis Spectrum { get; init; } = SpectrumAnalysis.Empty;
    public OriginSignals Origin { get; init; } = OriginSignals.Empty;
    public RipLogVerification Log { get; init; } = RipLogVerification.Empty;
    public IReadOnlyList<string> CueWarnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ParserWarnings { get; init; } = Array.Empty<string>();
    public VerificationStatus Status { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}
