namespace RipVerify.Core.Models;

public enum VerificationStatus
{
    VerifiedCdRip,
    VerifiedWithWarnings,
    AuthenticRipWithLog,
    LikelyStreaming,
    UndeterminedLossless,
    FakeLossless,
    Unprocessable
}
