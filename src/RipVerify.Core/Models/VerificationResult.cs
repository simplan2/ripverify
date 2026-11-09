namespace RipVerify.Core.Models;

public sealed record VerificationResult
{
    public string AlbumPath { get; init; } = string.Empty;
    public string? CuePath { get; init; }
    public CueOrigin CueOrigin { get; init; } = CueOrigin.Original;
    public DbPowerampEvidence DbPoweramp { get; init; } = DbPowerampEvidence.Empty;
    public AccurateRipStatus AccurateRipStatus { get; init; }
    public int ConfidenceScore { get; init; }
    public CtdbStatus CtdbStatus { get; init; } = CtdbStatus.Unprocessable;
    public int CtdbConfidenceScore { get; init; }
    public int CtdbMatches { get; init; }
    public int CtdbMismatches { get; init; }
    public int? DriveOffset { get; init; }
    public bool CtdbParityAvailable { get; init; }
    public SpectrumAnalysis Spectrum { get; init; } = SpectrumAnalysis.Empty;
    public OriginSignals Origin { get; init; } = OriginSignals.Empty;
    public RipLogVerification Log { get; init; } = RipLogVerification.Empty;
    public VerificationStatus Status { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public DateTimeOffset VerifiedAt { get; init; }
    public bool StatusIsPreliminary { get; init; }
    public IReadOnlyList<string> ParserWarnings { get; init; } = Array.Empty<string>();
}
