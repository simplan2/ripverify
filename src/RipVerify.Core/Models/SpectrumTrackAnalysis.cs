namespace RipVerify.Core.Models;

public sealed record SpectrumTrackAnalysis
{
    public string FilePath { get; init; } = string.Empty;
    public double NyquistCutoffHz { get; init; }
    public double BrickwallScore { get; init; }
    public double RawBrickwallScore { get; init; }
    public bool IsBrickwallSuspected { get; init; }
    public bool MasteringFilterDetected { get; init; }
}
