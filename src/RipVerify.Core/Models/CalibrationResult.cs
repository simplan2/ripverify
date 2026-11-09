namespace RipVerify.Core.Models;

/// <summary>
/// Outcome of a calibration pass over the calibration corpus.
/// </summary>
public sealed record CalibrationResult
{
    public double? Threshold { get; init; }
    public double F1Score { get; init; }
    public int FalsePositivesCount { get; init; }
    public int FalseNegativesCount { get; init; }
    public int TruePositivesCount { get; init; }
    public int TrueNegativesCount { get; init; }

    /// <summary>
    /// A threshold is accepted only when it produces zero false positives.
    /// </summary>
    public bool IsAccepted => Threshold.HasValue && FalsePositivesCount == 0;
}
