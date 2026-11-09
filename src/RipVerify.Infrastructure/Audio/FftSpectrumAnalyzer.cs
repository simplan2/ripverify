using MathNet.Numerics.IntegralTransforms;
using System.Numerics;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using Serilog;

namespace RipVerify.Infrastructure.Audio;

public sealed class FftSpectrumAnalyzer : ISpectrumAnalyzer
{
    private const double KneeSearchStartHz = 8000.0;
    private const double SmoothingWindowHz = 150.0;
    private const double TransitionBeforeHz = 500.0;
    private const double TransitionAfterHz = 2000.0;
    private const double EnergyBeforeHz = 1000.0;
    private const double EnergyAfterHz = 2000.0;
    private const double EnergyPostOffsetHz = 1000.0;
    private const double FlatnessAfterCutoffOffsetHz = 2000.0;
    private const double PassbandReferenceStartHz = 6000.0;
    private const double PassbandReferenceEndHz = 12000.0;
    private const double PassbandToleranceDb = 10.0;
    private const double KneeDropDb = 6.0;
    private const double BrickwallRefinementSlopeDbPerKHz = 20.0;
    private const double SlopeMeasurementBeforeHz = 3000.0;
    private const double SlopeMeasurementAfterHz = 3000.0;

    private readonly FfmpegPcmReader _ffmpegReader;

    public FftSpectrumAnalyzer(FfmpegPcmReader ffmpegReader)
    {
        _ffmpegReader = ffmpegReader;
    }

    public async Task<SpectrumAnalysis> AnalyzeAlbumAsync(
        AlbumMetadata album,
        SpectrumAnalysisOptions options,
        CancellationToken cancellationToken)
    {
        if (album is null)
        {
            return SpectrumAnalysis.Empty;
        }

        var incompleteTracks = album.Tracks
            .Where(track => track.IsIncomplete || track.Warnings.Contains("TrackIncomplete", StringComparer.Ordinal))
            .ToList();
        if (incompleteTracks.Count > 0)
        {
            Log.Information(
                "Album spectrum analysis ignoring {Count} incomplete track(s): {Tracks}",
                incompleteTracks.Count,
                string.Join("; ", incompleteTracks.Select(track => track.FilePath)));
        }

        var trackPaths = album.Tracks
            .Where(track => !track.IsIncomplete && !track.Warnings.Contains("TrackIncomplete", StringComparer.Ordinal))
            .Select(track => track.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (trackPaths.Count == 0)
        {
            Log.Warning("Album spectrum analysis skipped: no complete track paths available for album {Album}", album.Album);
            return SpectrumAnalysis.Empty;
        }

        if (trackPaths.Count == 1)
        {
            return await AnalyzeAsync(trackPaths[0], options, cancellationToken).ConfigureAwait(false);
        }

        var perTrack = new List<(string Path, SpectrumAnalysis Analysis)>();
        foreach (var trackPath in trackPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var analysis = await AnalyzeAsync(trackPath, options, cancellationToken).ConfigureAwait(false);
            if (analysis.BandEnergyDb.Count == 0)
            {
                Log.Warning("Album spectrum analysis skipped unreadable track: {TrackPath}", trackPath);
                continue;
            }

            perTrack.Add((trackPath, analysis));
        }

        if (perTrack.Count == 0)
        {
            Log.Warning("Album spectrum analysis failed for every track of album {Album}", album.Album);
            return SpectrumAnalysis.Empty;
        }

        // The "worst" track is the one with the lowest cutoff, because that is the track the
        // user needs to identify when the aggregate reports a low NyquistCutoffHz. The album
        // BrickwallScore still uses the maximum so the most suspicious track is never hidden.
        var worst = perTrack
            .OrderBy(item => item.Analysis.NyquistCutoffHz)
            .ThenByDescending(item => item.Analysis.BrickwallScore)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .First();

        var minimumNyquist = perTrack.Min(item => item.Analysis.NyquistCutoffHz);
        var maximumScore = perTrack.Max(item => item.Analysis.BrickwallScore);
        var maximumRawScore = perTrack.Max(item => item.Analysis.RawBrickwallScore);
        var masteringFilterDetected = perTrack.Any(item => item.Analysis.MasteringFilterDetected);
        var bandCount = perTrack.Min(item => item.Analysis.BandEnergyDb.Count);
        var averagedBands = new List<double>(bandCount);
        for (var band = 0; band < bandCount; band++)
        {
            averagedBands.Add(perTrack.Average(item => item.Analysis.BandEnergyDb[band]));
        }

        Log.Information(
            "Album spectrum analysis completed: Album={Album} Tracks={TrackCount} WorstTrack={WorstTrack} " +
            "MaxBrickwallScore={BrickwallScore:F3} MaxRawBrickwallScore={RawBrickwallScore:F3} " +
            "MasteringFilterDetected={MasteringFilterDetected} MinNyquistCutoffHz={NyquistCutoffHz:F0}",
            album.Album,
            perTrack.Count,
            worst.Path,
            maximumScore,
            maximumRawScore,
            masteringFilterDetected,
            minimumNyquist);

        return new SpectrumAnalysis
        {
            NyquistCutoffHz = minimumNyquist,
            BrickwallScore = maximumScore,
            RawBrickwallScore = maximumRawScore,
            IsBrickwallSuspected = perTrack.Any(item => item.Analysis.IsBrickwallSuspected),
            MasteringFilterDetected = masteringFilterDetected,
            BandEnergyDb = averagedBands,
            BandEdgesHz = options.BandEdgesHz,
            Tracks = perTrack
                .Select(item => new SpectrumTrackAnalysis
                {
                    FilePath = item.Path,
                    NyquistCutoffHz = item.Analysis.NyquistCutoffHz,
                    BrickwallScore = item.Analysis.BrickwallScore,
                    RawBrickwallScore = item.Analysis.RawBrickwallScore,
                    IsBrickwallSuspected = item.Analysis.IsBrickwallSuspected,
                    MasteringFilterDetected = item.Analysis.MasteringFilterDetected
                })
                .ToList(),
            SpectralFlatness = worst.Analysis.SpectralFlatness,
            RolloffSlopeDbPerKHz = worst.Analysis.RolloffSlopeDbPerKHz,
            WorstTrackFilePath = worst.Path
        };
    }

    public async Task<SpectrumAnalysis> AnalyzeAsync(string audioFilePath, SpectrumAnalysisOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath) || !File.Exists(audioFilePath))
        {
            Log.Warning("Audio file not found for spectrum analysis: {Path}", audioFilePath);
            return SpectrumAnalysis.Empty;
        }

        try
        {
            Log.Debug("Starting spectrum analysis for {AudioFilePath}", audioFilePath);

            var pcm = await _ffmpegReader.ReadMono44100Async(audioFilePath, cancellationToken);
            if (pcm == null || pcm.Length == 0)
            {
                Log.Warning(
                    "Failed to decode audio or empty PCM for spectrum analysis: {AudioFilePath}. Check the configured FFmpeg path and the FFmpeg warnings above.",
                    audioFilePath);
                return SpectrumAnalysis.Empty;
            }

            if (pcm.Length < 44100 * 2) // less than 2 seconds
            {
                Log.Warning(
                    "Audio too short for spectrum analysis: {AudioFilePath} Samples={SampleCount}",
                    audioFilePath,
                    pcm.Length);
                return SpectrumAnalysis.Empty;
            }

            var fftSize = options.FftSize;
            var hopSize = options.HopSize;
            var sampleRate = options.SampleRateHint;

            int frameCount = Math.Max(1, (pcm.Length - fftSize) / hopSize + 1);
            var magnitudeSum = new double[fftSize / 2 + 1];

            for (int frame = 0; frame < frameCount; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var complex = new Complex[fftSize];
                for (int i = 0; i < fftSize; i++)
                {
                    int idx = frame * hopSize + i;
                    if (idx < pcm.Length)
                    {
                        var window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (fftSize - 1)));
                        complex[i] = new Complex(pcm[idx] * window, 0);
                    }
                    else
                    {
                        complex[i] = Complex.Zero;
                    }
                }

                Fourier.Forward(complex, FourierOptions.Matlab);

                for (int i = 0; i < magnitudeSum.Length; i++)
                {
                    magnitudeSum[i] += complex[i].Magnitude;
                }
            }

            for (int i = 0; i < magnitudeSum.Length; i++)
            {
                magnitudeSum[i] = magnitudeSum[i] / frameCount;
            }

            var bandEnergies = ComputeBandEnergies(magnitudeSum, options, fftSize, sampleRate);

            double peak = magnitudeSum.Max();
            if (peak <= 0 || double.IsNaN(peak) || double.IsInfinity(peak))
            {
                Log.Warning(
                    "FFT produced no usable energy for spectrum analysis: {AudioFilePath} Peak={Peak}",
                    audioFilePath,
                    peak);
                return SpectrumAnalysis.Empty;
            }

            var power = new double[magnitudeSum.Length];
            for (int i = 0; i < magnitudeSum.Length; i++)
            {
                power[i] = magnitudeSum[i] * magnitudeSum[i];
            }

            var dbSpectrum = ToDecibels(power);
            var smoothingBins = Math.Max(3, Odd((int)Math.Round(SmoothingWindowHz * fftSize / sampleRate)));
            var smoothedDb = MovingAverage(dbSpectrum, smoothingBins);

            var nyquistHz = sampleRate / 2.0;
            var kneeHz = nyquistHz;
            var searchStartBin = (int)(KneeSearchStartHz * fftSize / sampleRate);
            var searchEndBin = (int)((nyquistHz - 500.0) * fftSize / sampleRate);
            var beforeBins = (int)(TransitionBeforeHz * fftSize / sampleRate);
            var afterBins = (int)(TransitionAfterHz * fftSize / sampleRate);

            var referenceLowBin = FrequencyToBin(PassbandReferenceStartHz, fftSize, sampleRate);
            var referenceHighBin = FrequencyToBin(PassbandReferenceEndHz, fftSize, sampleRate);
            referenceLowBin = Math.Max(1, Math.Min(referenceLowBin, smoothedDb.Length - 2));
            referenceHighBin = Math.Max(referenceLowBin + 1, Math.Min(referenceHighBin, smoothedDb.Length - 1));
            var passbandReferenceDb = MeanDecibels(power, referenceLowBin, referenceHighBin);

            var hasKnee = false;
            var onsetHz = nyquistHz;
            for (var bin = Math.Max(1, searchStartBin); bin <= searchEndBin; bin++)
            {
                var preLowBin = FrequencyToBin((bin * sampleRate / (double)fftSize) - EnergyBeforeHz, fftSize, sampleRate);
                var preHighBin = FrequencyToBin((bin * sampleRate / (double)fftSize) - 200.0, fftSize, sampleRate);
                var postLowBin = FrequencyToBin((bin * sampleRate / (double)fftSize) + 200.0, fftSize, sampleRate);
                var postHighBin = FrequencyToBin((bin * sampleRate / (double)fftSize) + EnergyAfterHz, fftSize, sampleRate);

                preLowBin = Math.Max(1, Math.Min(preLowBin, power.Length - 2));
                preHighBin = Math.Max(preLowBin + 1, Math.Min(preHighBin, power.Length - 1));
                postLowBin = Math.Max(1, Math.Min(postLowBin, power.Length - 2));
                postHighBin = Math.Max(postLowBin + 1, Math.Min(postHighBin, power.Length - 1));

                var preDb = MeanDecibels(power, preLowBin, preHighBin);
                if (preDb < passbandReferenceDb - PassbandToleranceDb)
                {
                    continue;
                }

                var postDb = MeanDecibels(power, postLowBin, postHighBin);
                if (preDb - postDb >= KneeDropDb)
                {
                    onsetHz = bin * sampleRate / (double)fftSize;
                    hasKnee = true;
                    break;
                }
            }

            var bestSlopeDbPerKHz = 0.0;
            if (hasKnee)
            {
                var onsetBin = FrequencyToBin(onsetHz, fftSize, sampleRate);
                var refineLowBin = Math.Max(searchStartBin, onsetBin - beforeBins);
                var refineHighBin = Math.Min(searchEndBin, onsetBin + afterBins);
                var bestSlopeBin = onsetBin;

                for (var bin = refineLowBin; bin <= refineHighBin; bin++)
                {
                    var lowBin = Math.Max(searchStartBin, bin - beforeBins);
                    var highBin = Math.Min(magnitudeSum.Length - 2, bin + afterBins);
                    if (highBin - lowBin < 8)
                    {
                        continue;
                    }

                    var slope = LinearSlopeDbPerKHz(smoothedDb, lowBin, highBin, fftSize, sampleRate);
                    if (slope < bestSlopeDbPerKHz)
                    {
                        bestSlopeDbPerKHz = slope;
                        bestSlopeBin = bin;
                    }
                }

                kneeHz = Math.Abs(bestSlopeDbPerKHz) >= BrickwallRefinementSlopeDbPerKHz
                    ? bestSlopeBin * sampleRate / (double)fftSize
                    : onsetHz;
            }

            double energyDropDb = 0;
            double rolloffSlopeDbPerKHz = 0;
            double flatnessAfterCutoff = 0;

            if (hasKnee)
            {
                var preLowBin = FrequencyToBin(kneeHz - EnergyBeforeHz, fftSize, sampleRate);
                var preHighBin = FrequencyToBin(kneeHz - 200.0, fftSize, sampleRate);
                var postLowBin = FrequencyToBin(kneeHz + EnergyPostOffsetHz, fftSize, sampleRate);
                var postHighBin = FrequencyToBin(kneeHz + EnergyAfterHz, fftSize, sampleRate);
                preLowBin = Math.Max(1, preLowBin);
                preHighBin = Math.Min(magnitudeSum.Length - 1, preHighBin);
                postLowBin = Math.Max(1, postLowBin);
                postHighBin = Math.Min(magnitudeSum.Length - 1, postHighBin);

                if (postHighBin > postLowBin && preHighBin > preLowBin)
                {
                    var preDb = MeanDecibels(power, preLowBin, preHighBin);
                    var postDb = MeanDecibels(power, postLowBin, postHighBin);
                    energyDropDb = Math.Max(0, preDb - postDb);
                }

                // Measure the average rolloff across a wide band around the knee. Using only the
                // steepest local window overstates gentle mastering rolloffs from the 90s while
                // still saturating for the near-vertical wall of a lossy transcode.
                var wideSlopeLowBin = Math.Max(1, FrequencyToBin(kneeHz - SlopeMeasurementBeforeHz, fftSize, sampleRate));
                var wideSlopeHighBin = Math.Min(
                    magnitudeSum.Length - 2,
                    FrequencyToBin(kneeHz + SlopeMeasurementAfterHz, fftSize, sampleRate));
                if (wideSlopeHighBin - wideSlopeLowBin >= 8)
                {
                    rolloffSlopeDbPerKHz = Math.Abs(
                        LinearSlopeDbPerKHz(smoothedDb, wideSlopeLowBin, wideSlopeHighBin, fftSize, sampleRate));
                }

                var flatLowBin = FrequencyToBin(kneeHz + FlatnessAfterCutoffOffsetHz, fftSize, sampleRate);
                var flatHighBin = FrequencyToBin(nyquistHz - 200.0, fftSize, sampleRate);
                flatLowBin = Math.Max(1, flatLowBin);
                flatHighBin = Math.Min(magnitudeSum.Length - 1, flatHighBin);

                if (flatHighBin - flatLowBin >= 8)
                {
                    flatnessAfterCutoff = ComputeFlatnessAfterCutoff(smoothedDb, flatLowBin, flatHighBin);
                }
            }
            else
            {
                kneeHz = nyquistHz;
            }

            var rawBrickwallScore = ComputeBrickwallScore(energyDropDb, rolloffSlopeDbPerKHz, flatnessAfterCutoff);
            var highCutoff = kneeHz > SpectrumThresholds.HighCutoffGuardHz;
            var masteringFilterDetected = highCutoff && rawBrickwallScore >= options.BrickwallThreshold;
            var brickwallScore = highCutoff
                ? rawBrickwallScore * SpectrumThresholds.HighCutoffPenaltyMultiplier
                : rawBrickwallScore;
            var isBrickwallSuspected = !masteringFilterDetected && brickwallScore >= options.BrickwallThreshold;

            Log.Debug(
                "Spectrum analysis completed: {AudioFilePath} Samples={SampleCount} Frames={FrameCount} " +
                "KneeHz={KneeHz:F0} EnergyDropDb={EnergyDropDb:F1} RolloffSlopeDbPerKHz={RolloffSlopeDbPerKHz:F1} " +
                "FlatnessAfterCutoff={FlatnessAfterCutoff:F2} RawBrickwallScore={RawBrickwallScore:F3} " +
                "BrickwallScore={BrickwallScore:F3} MasteringFilterDetected={MasteringFilterDetected} Bands={BandCount}",
                audioFilePath,
                pcm.Length,
                frameCount,
                kneeHz,
                energyDropDb,
                rolloffSlopeDbPerKHz,
                flatnessAfterCutoff,
                rawBrickwallScore,
                brickwallScore,
                masteringFilterDetected,
                bandEnergies.Count);

            return new SpectrumAnalysis
            {
                NyquistCutoffHz = kneeHz,
                BrickwallScore = brickwallScore,
                RawBrickwallScore = rawBrickwallScore,
                IsBrickwallSuspected = isBrickwallSuspected,
                MasteringFilterDetected = masteringFilterDetected,
                BandEnergyDb = bandEnergies,
                BandEdgesHz = options.BandEdgesHz,
                Tracks = new[]
                {
                    new SpectrumTrackAnalysis
                    {
                        FilePath = audioFilePath,
                        NyquistCutoffHz = kneeHz,
                        BrickwallScore = brickwallScore,
                        RawBrickwallScore = rawBrickwallScore,
                        IsBrickwallSuspected = isBrickwallSuspected,
                        MasteringFilterDetected = masteringFilterDetected
                    }
                },
                SpectralFlatness = flatnessAfterCutoff,
                RolloffSlopeDbPerKHz = rolloffSlopeDbPerKHz
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Spectrum analysis failed for {AudioFilePath}", audioFilePath);
            return SpectrumAnalysis.Empty;
        }
    }

    /// <summary>
    /// Computes the raw brickwall score with the fixed three-component weighted formula:
    /// 0.50 * normalized energy drop + 0.30 * normalized rolloff slope + 0.20 * flatness after cutoff.
    /// A gradual mastering rolloff (for example 15 kHz without a vertical wall) stays well below
    /// the threshold, while a lossy-codec style wall approaches 1.0. The caller applies the
    /// high-cutoff mastering-filter penalty separately.
    /// </summary>
    /// <param name="energyDropDb">Energy difference in dB between the bands before and after the cutoff.</param>
    /// <param name="rolloffSlopeDbPerKHz">Magnitude of the rolloff slope in dB per kHz.</param>
    /// <param name="flatnessAfterCutoff">
    /// 1 - min(variance of the post-cutoff energy / 50, 1). Flat, nearly silent energy after the
    /// cutoff yields values close to 1; a gradual decline yields lower values.
    /// </param>
    internal static double ComputeBrickwallScore(double energyDropDb, double rolloffSlopeDbPerKHz, double flatnessAfterCutoff)
    {
        var normalizedEnergyDrop = Math.Min(Math.Max(energyDropDb, 0) / 40.0, 1.0);
        var slopeRatio = Math.Min(Math.Abs(rolloffSlopeDbPerKHz) / 15.0, 1.0);
        var flatness = Math.Clamp(flatnessAfterCutoff, 0.0, 1.0);

        return (0.50 * normalizedEnergyDrop) + (0.30 * slopeRatio) + (0.20 * flatness);
    }

    private static List<double> ComputeBandEnergies(double[] magnitudeSum, SpectrumAnalysisOptions options, int fftSize, int sampleRate)
    {
        var bandEnergies = new List<double>();
        var bandEdges = options.BandEdgesHz.ToList();
        bandEdges.Insert(0, 0);
        for (int b = 0; b < options.BandEdgesHz.Count; b++)
        {
            double lowFreq = bandEdges[b];
            double highFreq = bandEdges[b + 1];
            int lowBin = Math.Max(1, FrequencyToBin(lowFreq, fftSize, sampleRate));
            int highBin = Math.Min(magnitudeSum.Length - 1, FrequencyToBin(highFreq, fftSize, sampleRate));
            double sum = 0;
            for (int i = lowBin; i <= highBin; i++)
            {
                sum += magnitudeSum[i];
            }

            double avg = highBin >= lowBin ? sum / (highBin - lowBin + 1) : 0;
            bandEnergies.Add(20 * Math.Log10(avg + 1e-12));
        }

        return bandEnergies;
    }

    private static double[] ToDecibels(double[] power)
    {
        var db = new double[power.Length];
        for (var i = 0; i < power.Length; i++)
        {
            db[i] = 10 * Math.Log10(power[i] + 1e-20);
        }

        return db;
    }

    private static double[] MovingAverage(double[] values, int window)
    {
        var result = new double[values.Length];
        var half = window / 2;
        for (var i = 0; i < values.Length; i++)
        {
            var low = Math.Max(0, i - half);
            var high = Math.Min(values.Length - 1, i + half);
            double sum = 0;
            for (var j = low; j <= high; j++)
            {
                sum += values[j];
            }

            result[i] = sum / (high - low + 1);
        }

        return result;
    }

    private static double LinearSlopeDbPerKHz(double[] db, int lowBin, int highBin, int fftSize, int sampleRate)
    {
        double sumX = 0, sumY = 0, sumXy = 0, sumXx = 0;
        int count = highBin - lowBin + 1;
        for (var bin = lowBin; bin <= highBin; bin++)
        {
            var x = bin * sampleRate / (double)fftSize / 1000.0;
            var y = db[bin];
            sumX += x;
            sumY += y;
            sumXy += x * y;
            sumXx += x * x;
        }

        var denominator = (count * sumXx) - (sumX * sumX);
        if (Math.Abs(denominator) < 1e-12)
        {
            return 0;
        }

        return ((count * sumXy) - (sumX * sumY)) / denominator;
    }

    private static double MeanDecibels(double[] power, int lowBin, int highBin)
    {
        double sum = 0;
        for (var i = lowBin; i <= highBin; i++)
        {
            sum += power[i];
        }

        var average = sum / (highBin - lowBin + 1);
        return 10 * Math.Log10(average + 1e-20);
    }

    private static double ComputeFlatnessAfterCutoff(double[] db, int lowBin, int highBin)
    {
        double sum = 0;
        var count = highBin - lowBin + 1;
        for (var i = lowBin; i <= highBin; i++)
        {
            sum += db[i];
        }

        var mean = sum / count;
        double varianceSum = 0;
        for (var i = lowBin; i <= highBin; i++)
        {
            var difference = db[i] - mean;
            varianceSum += difference * difference;
        }

        var variance = varianceSum / count;
        return 1.0 - Math.Min(variance / 50.0, 1.0);
    }

    private static int FrequencyToBin(double frequencyHz, int fftSize, int sampleRate)
        => (int)Math.Round(frequencyHz * fftSize / sampleRate);

    private static int Odd(int value) => value % 2 == 0 ? value + 1 : value;
}
