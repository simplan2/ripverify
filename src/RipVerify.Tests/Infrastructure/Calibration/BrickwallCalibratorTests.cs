namespace RipVerify.Tests.Infrastructure.Calibration;

using FluentAssertions;
using RipVerify.Core.Models;
using RipVerify.Infrastructure.Calibration;
using Xunit;

public sealed class BrickwallCalibratorTests
{
    [Fact]
    public void CalibrateWithMetrics_FindsThresholdWithoutFalsePositives()
    {
        var corpus = Corpus(
            Entry(CalibrationEntry.CdOrigin, 0.10),
            Entry(CalibrationEntry.CdOrigin, 0.20),
            Entry(CalibrationEntry.CdOrigin, 0.30),
            Entry(CalibrationEntry.CdNaturalRolloffOrigin, 0.35),
            Entry(CalibrationEntry.CdNaturalRolloffOrigin, 0.45),
            Entry(CalibrationEntry.VinylOrigin, 0.25),
            Entry(CalibrationEntry.StreamingOrigin, 0.15),
            Entry(CalibrationEntry.TranscodeOrigin, 0.75),
            Entry(CalibrationEntry.TranscodeOrigin, 0.80),
            Entry(CalibrationEntry.TranscodeOrigin, 0.85),
            Entry(CalibrationEntry.TranscodeOrigin, 0.90));
        var entries = corpus.Entries.ToList();
        entries.Add(Entry(CalibrationEntry.CdOrigin, 0.05));
        corpus = new CalibrationCorpus { Entries = entries };

        var result = BrickwallCalibrator.CalibrateWithMetrics(corpus);

        result.IsAccepted.Should().BeTrue();
        result.FalsePositivesCount.Should().Be(0);
        result.FalseNegativesCount.Should().Be(0);
        result.F1Score.Should().BeApproximately(1.0, 0.001);
        result.Threshold.Should().BeGreaterThan(0.45).And.BeLessThanOrEqualTo(0.75);
    }

    [Fact]
    public void CalibrateWithMetrics_TreatsCdNaturalRolloffAsNonTranscode()
    {
        var entries = new List<CalibrationEntry>();
        for (var i = 0; i < 9; i++)
        {
            entries.Add(Entry(CalibrationEntry.CdNaturalRolloffOrigin, 0.55));
        }

        entries.Add(Entry(CalibrationEntry.TranscodeOrigin, 0.90));

        var result = BrickwallCalibrator.CalibrateWithMetrics(new CalibrationCorpus { Entries = entries });

        result.IsAccepted.Should().BeTrue();
        result.F1Score.Should().BeApproximately(1.0, 0.001);
        result.TrueNegativesCount.Should().Be(9);
        result.FalsePositivesCount.Should().Be(0);
        result.FalseNegativesCount.Should().Be(0);
    }

    [Fact]
    public void CalibrateWithMetrics_ExcludesUnknownOriginFromCalculation()
    {
        var entries = new List<CalibrationEntry>();
        for (var i = 0; i < 5; i++)
        {
            entries.Add(Entry(CalibrationEntry.CdOrigin, 0.20));
        }

        for (var i = 0; i < 5; i++)
        {
            entries.Add(Entry(CalibrationEntry.TranscodeOrigin, 0.90));
        }

        entries.Add(Entry(CalibrationEntry.UnknownOrigin, 0.99));
        entries.Add(Entry(CalibrationEntry.UnknownOrigin, 0.01));

        var result = BrickwallCalibrator.CalibrateWithMetrics(new CalibrationCorpus { Entries = entries });

        result.IsAccepted.Should().BeTrue();
        result.FalsePositivesCount.Should().Be(0);
        result.FalseNegativesCount.Should().Be(0);
    }

    [Fact]
    public void CalibrateWithMetrics_RejectsThresholdWhenFalsePositivesCannotBeAvoided()
    {
        var entries = new List<CalibrationEntry>();
        for (var i = 0; i < 6; i++)
        {
            entries.Add(Entry(CalibrationEntry.CdOrigin, 0.80));
        }

        for (var i = 0; i < 6; i++)
        {
            entries.Add(Entry(CalibrationEntry.TranscodeOrigin, 0.50));
        }

        var result = BrickwallCalibrator.CalibrateWithMetrics(new CalibrationCorpus { Entries = entries });

        result.IsAccepted.Should().BeFalse();
        result.Threshold.Should().BeNull();
    }

    [Fact]
    public void CalibrateWithMetrics_ReturnsNoThresholdForSmallCorpus()
    {
        var corpus = Corpus(
            Entry(CalibrationEntry.CdOrigin, 0.10),
            Entry(CalibrationEntry.TranscodeOrigin, 0.90));

        var result = BrickwallCalibrator.CalibrateWithMetrics(corpus);

        result.Threshold.Should().BeNull();
        result.IsAccepted.Should().BeFalse();
    }

    [Fact]
    public void Calibrate_ReturnsAcceptedThreshold()
    {
        var corpus = Corpus(
            Entry(CalibrationEntry.CdOrigin, 0.10),
            Entry(CalibrationEntry.CdOrigin, 0.20),
            Entry(CalibrationEntry.CdOrigin, 0.30),
            Entry(CalibrationEntry.CdOrigin, 0.40),
            Entry(CalibrationEntry.CdOrigin, 0.45),
            Entry(CalibrationEntry.CdNaturalRolloffOrigin, 0.50),
            Entry(CalibrationEntry.TranscodeOrigin, 0.80),
            Entry(CalibrationEntry.TranscodeOrigin, 0.85),
            Entry(CalibrationEntry.TranscodeOrigin, 0.90),
            Entry(CalibrationEntry.TranscodeOrigin, 0.95));

        var threshold = BrickwallCalibrator.Calibrate(corpus);

        threshold.Should().NotBeNull();
        threshold!.Value.Should().BeGreaterThan(0.50).And.BeLessThanOrEqualTo(0.80);
    }

    private static CalibrationEntry Entry(string origin, double score)
        => new()
        {
            FilePath = $"audio/{origin}-{score:F2}.flac",
            ExpectedOrigin = origin,
            ObservedBrickwallScore = score,
            ObservedNyquistCutoffHz = 22050,
            ObservedRolloffSlopeDbPerKHz = 0.5,
            ObservedSpectralFlatness = 0.2,
            TaggedAt = DateTimeOffset.UtcNow
        };

    private static CalibrationCorpus Corpus(params CalibrationEntry[] entries)
        => new() { Entries = entries };
}
