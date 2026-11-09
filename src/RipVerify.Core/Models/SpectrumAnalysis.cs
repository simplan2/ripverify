namespace RipVerify.Core.Models;

public sealed record SpectrumAnalysis
{
    public double NyquistCutoffHz { get; init; }
    public double BrickwallScore { get; init; }
    public double RawBrickwallScore { get; init; }
    public bool IsBrickwallSuspected { get; init; }
    public bool MasteringFilterDetected { get; init; }
    public IReadOnlyList<double> BandEnergyDb { get; init; } = Array.Empty<double>();
    public IReadOnlyList<double> BandEdgesHz { get; init; } = Array.Empty<double>();
    public IReadOnlyList<SpectrumTrackAnalysis> Tracks { get; init; } = Array.Empty<SpectrumTrackAnalysis>();
    public double SpectralFlatness { get; init; }
    public double RolloffSlopeDbPerKHz { get; init; }

    /// <summary>
    /// Track with the lowest cutoff when the album was analyzed track by track, so the
    /// track responsible for <see cref="NyquistCutoffHz"/> can be identified. The album
    /// <see cref="BrickwallScore"/> is still the maximum across tracks.
    /// Null for single-track analysis or Empty.
    /// </summary>
    public string? WorstTrackFilePath { get; init; }

    public static SpectrumAnalysis Empty { get; } = new();
}
