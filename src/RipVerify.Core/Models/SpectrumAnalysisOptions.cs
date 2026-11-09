namespace RipVerify.Core.Models;

public sealed record SpectrumAnalysisOptions
{
    public int FftSize { get; init; } = 8192;
    public int HopSize { get; init; } = 4096;
    public int SampleRateHint { get; init; } = 44100;
    public double NyquistToleranceHz { get; init; } = 500.0;
    public double BrickwallThreshold { get; init; } = 0.65; // provisional; must be recalibrated with real corpus
    public IReadOnlyList<double> BandEdgesHz { get; init; } = new double[]
    {
        1000, 2000, 4000, 8000, 12000, 14000, 16000, 18000, 19000, 20000, 21000, 22050
    };
}
