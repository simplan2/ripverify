using RipVerify.Core.Models;
using Serilog;

namespace RipVerify.Infrastructure.Calibration;

public sealed class BrickwallCalibrator
{
    /// <summary>
    /// Backwards-compatible entry point that returns the calibrated threshold, or null when
    /// the corpus is too small or no threshold without false positives can be found.
    /// </summary>
    public static double? Calibrate(CalibrationCorpus corpus) => CalibrateWithMetrics(corpus).Threshold;

    /// <summary>
    /// Computes the threshold that maximizes F1 while requiring zero false positives.
    /// Entries tagged "unknown" are excluded from the calculation. "cd-natural-rolloff"
    /// is treated as a non-transcode, exactly like "cd" and "vinyl".
    /// </summary>
    public static CalibrationResult CalibrateWithMetrics(CalibrationCorpus corpus)
    {
        var entries = corpus.Entries
            .Where(entry => !string.Equals(entry.ExpectedOrigin, CalibrationEntry.UnknownOrigin, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (entries.Count < 10)
        {
            Log.Warning("Calibration corpus has less than 10 usable entries, cannot calibrate reliably");
            return new CalibrationResult();
        }

        var candidates = entries
            .Select(entry => entry.ObservedBrickwallScore)
            .Distinct()
            .OrderBy(score => score)
            .ToList();

        if (candidates.Count > 0)
        {
            candidates.Add(candidates[^1] + 0.000001);
        }

        CalibrationResult? bestAccepted = null;
        CalibrationResult? bestOverall = null;

        foreach (var threshold in candidates)
        {
            var result = Evaluate(entries, threshold);
            if (bestOverall is null || result.F1Score > bestOverall.F1Score)
            {
                bestOverall = result;
            }

            if (result.FalsePositivesCount == 0 && result.TruePositivesCount > 0 &&
                (bestAccepted is null || result.F1Score > bestAccepted.F1Score))
            {
                bestAccepted = result;
            }
        }

        if (bestAccepted is null)
        {
            var rejected = bestOverall ?? new CalibrationResult();
            Log.Warning(
                "Calibration_FalsePositivesDetected: no threshold without false positives; best threshold {Threshold} had {FalsePositives} false positives",
                rejected.Threshold,
                rejected.FalsePositivesCount);
            return rejected with { Threshold = null };
        }

        Log.Information(
            "Calibrated brickwall threshold: {Threshold} with F1: {F1}, false positives: {FalsePositives}, false negatives: {FalseNegatives}",
            bestAccepted.Threshold,
            bestAccepted.F1Score,
            bestAccepted.FalsePositivesCount,
            bestAccepted.FalseNegativesCount);
        return bestAccepted;
    }

    private static CalibrationResult Evaluate(IReadOnlyList<CalibrationEntry> entries, double threshold)
    {
        double truePositives = 0;
        double falsePositives = 0;
        double trueNegatives = 0;
        double falseNegatives = 0;

        foreach (var entry in entries)
        {
            var predicted = entry.ObservedBrickwallScore >= threshold;
            var actual = IsTranscodeLike(entry.ExpectedOrigin);

            if (predicted && actual)
            {
                truePositives++;
            }
            else if (predicted)
            {
                falsePositives++;
            }
            else if (actual)
            {
                falseNegatives++;
            }
            else
            {
                trueNegatives++;
            }
        }

        var precision = truePositives + falsePositives > 0
            ? truePositives / (truePositives + falsePositives)
            : 0;
        var recall = truePositives + falseNegatives > 0
            ? truePositives / (truePositives + falseNegatives)
            : 0;
        var f1 = precision + recall > 0 ? 2 * precision * recall / (precision + recall) : 0;

        return new CalibrationResult
        {
            Threshold = threshold,
            F1Score = f1,
            FalsePositivesCount = (int)falsePositives,
            FalseNegativesCount = (int)falseNegatives,
            TruePositivesCount = (int)truePositives,
            TrueNegativesCount = (int)trueNegatives
        };
    }

    private static bool IsTranscodeLike(string expectedOrigin)
        => string.Equals(expectedOrigin, CalibrationEntry.TranscodeOrigin, StringComparison.OrdinalIgnoreCase);
}
