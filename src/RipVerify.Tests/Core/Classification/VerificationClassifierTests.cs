using FluentAssertions;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Core.Services.Classification;
using System.Diagnostics.CodeAnalysis;
using Xunit;

[module: SuppressMessage("Style", "CA1707:Identifiers should not contain underscores", Justification = "Test method naming convention")]

namespace RipVerify.Tests.Core.Classification;

public class VerificationClassifierTests
{
    private readonly VerificationClassifier _classifier = new();

    private static VerificationResult PartialResult(AccurateRipStatus arStatus, int confidence = 0)
    {
        return new VerificationResult
        {
            AlbumPath = "/test/album.cue",
            AccurateRipStatus = arStatus,
            ConfidenceScore = confidence,
            DriveOffset = 0,
            CtdbParityAvailable = false,
            Status = VerificationStatus.UndeterminedLossless,
            StatusIsPreliminary = true,
            Diagnostics = new List<string>()
        };
    }

    [Fact]
    public void CA3_T1_AccuratelyRipped_Plus_SpectrumOK_Plus_OriginNoSignals_VerifiedCdRip()
    {
        var partial = PartialResult(AccurateRipStatus.AccuratelyRipped, 42);
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };
        var origin = OriginSignals.Empty;

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.VerifiedCdRip);
        result.StatusIsPreliminary.Should().BeFalse();
        result.Spectrum.Should().BeSameAs(spectrum);
        result.Origin.Should().BeSameAs(origin);
    }

    [Fact]
    public void CA3_T2_AccuratelyRipped_Plus_SpectrumOK_Plus_OriginStreaming_VerifiedCdRip()
    {
        var partial = PartialResult(AccurateRipStatus.AccuratelyRipped, 10);
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };
        var origin = OriginSignals.Empty with { StreamingConfidence = 0.9, HasStreamingOnlyTags = true };

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.VerifiedCdRip); // AR mandates
        result.StatusIsPreliminary.Should().BeFalse();
    }

    [Fact]
    public void CA3_T3_NotFound_Plus_SpectrumOK_Plus_OriginStreaming_LikelyStreaming()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0);
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };
        var origin = OriginSignals.Empty with { StreamingConfidence = 0.8 };

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.LikelyStreaming);
        result.StatusIsPreliminary.Should().BeFalse();
    }

    [Fact]
    public void CA3_T4_NotFound_Plus_SpectrumOK_Plus_OriginNoSignals_UndeterminedLossless()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0);
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };
        var origin = OriginSignals.Empty with { StreamingConfidence = 0.1 };

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.UndeterminedLossless);
        result.StatusIsPreliminary.Should().BeFalse();
    }

    [Fact]
    public void CA3_T5_Inaccurate_Plus_SpectrumOK_Plus_OriginStreaming_LikelyStreaming()
    {
        var partial = PartialResult(AccurateRipStatus.Inaccurate, 0);
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };
        var origin = OriginSignals.Empty with { StreamingConfidence = 0.6 };

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.LikelyStreaming);
        result.StatusIsPreliminary.Should().BeFalse();
    }

    [Fact]
    public void CA3_T6_Inaccurate_Plus_SpectrumOK_Plus_OriginNoSignals_UndeterminedLossless()
    {
        var partial = PartialResult(AccurateRipStatus.Inaccurate, 0);
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };
        var origin = OriginSignals.Empty with { StreamingConfidence = 0.2 };

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.UndeterminedLossless);
        result.StatusIsPreliminary.Should().BeFalse();
    }

    [Fact]
    public void CA3_T7_AccuratelyRipped_Plus_SpectrumBrickwall_FakeLossless()
    {
        var partial = PartialResult(AccurateRipStatus.AccuratelyRipped, 42);
        var spectrum = SpectrumAnalysis.Empty with { IsBrickwallSuspected = true, BrickwallScore = 0.9, NyquistCutoffHz = 16000 };
        var origin = OriginSignals.Empty;

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.FakeLossless); // brickwall mandates over AR
        result.StatusIsPreliminary.Should().BeFalse();
    }

    [Fact]
    public void CA3_T7b_AccuratelyRipped_Plus_HighCutoffBrickwall_VerifiedCdRip()
    {
        var partial = PartialResult(AccurateRipStatus.AccuratelyRipped, 42);
        var spectrum = SpectrumAnalysis.Empty with
        {
            IsBrickwallSuspected = true,
            BrickwallScore = 0.9,
            RawBrickwallScore = 1.0,
            NyquistCutoffHz = 19700,
            MasteringFilterDetected = true
        };
        var origin = OriginSignals.Empty;

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.VerifiedCdRip);
        result.StatusIsPreliminary.Should().BeFalse();
        result.Diagnostics.Should().Contain("Classifier_HighCutoffMasteringFilter");
    }

    [Fact]
    public void CA3_T7c_NotFound_Plus_HighCutoffBrickwall_DoesNotBecomeFakeLossless()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0);
        var spectrum = SpectrumAnalysis.Empty with
        {
            IsBrickwallSuspected = true,
            BrickwallScore = 0.9,
            NyquistCutoffHz = 19700
        };
        var origin = OriginSignals.Empty;

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.UndeterminedLossless);
    }

    [Fact]
    public void CA3_T8_NotFound_Plus_SpectrumBrickwall_FakeLossless()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0);
        var spectrum = SpectrumAnalysis.Empty with { IsBrickwallSuspected = true, BrickwallScore = 0.8 };
        var origin = OriginSignals.Empty;

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.FakeLossless);
        result.StatusIsPreliminary.Should().BeFalse();
    }

    [Fact]
    public void CA3_T9_Unprocessable_Plus_SpectrumOK_Unprocessable()
    {
        var partial = PartialResult(AccurateRipStatus.Unprocessable, 0);
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10 } };
        var origin = OriginSignals.Empty;

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.Unprocessable);
        result.StatusIsPreliminary.Should().BeFalse();
    }

    [Fact]
    public void LogCrcMatch_WithoutDatabaseMatch_AuthenticRipWithLog()
    {
        var partial = PartialResult(AccurateRipStatus.Inaccurate, 0) with
        {
            Log = VerifiedLog(isVirtual: false)
        };
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };

        var result = _classifier.Classify(partial, spectrum, OriginSignals.Empty);

        result.Status.Should().Be(VerificationStatus.AuthenticRipWithLog);
        result.Diagnostics.Should().Contain(d => d.Contains("AuthenticRipWithLog"));
    }

    [Fact]
    public void LogCrcMatch_OnVirtualDrive_UndeterminedLossless()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0) with
        {
            Log = VerifiedLog(isVirtual: true)
        };
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };

        var result = _classifier.Classify(partial, spectrum, OriginSignals.Empty);

        result.Status.Should().Be(VerificationStatus.UndeterminedLossless);
        result.Diagnostics.Should().Contain(d => d.Contains("virtual drive"));
    }

    [Fact]
    public void DatabaseMatch_BeatsLogEvidence()
    {
        var partial = PartialResult(AccurateRipStatus.AccuratelyRipped, 42) with
        {
            Log = VerifiedLog(isVirtual: false)
        };
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };

        var result = _classifier.Classify(partial, spectrum, OriginSignals.Empty);

        result.Status.Should().Be(VerificationStatus.VerifiedCdRip);
    }

    [Fact]
    public void Brickwall_BeatsLogEvidence()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0) with
        {
            Log = VerifiedLog(isVirtual: false)
        };
        var spectrum = SpectrumAnalysis.Empty with
        {
            BandEnergyDb = new List<double> { -10, -20 },
            IsBrickwallSuspected = true,
            BrickwallScore = 0.9,
            NyquistCutoffHz = 16000
        };

        var result = _classifier.Classify(partial, spectrum, OriginSignals.Empty);

        result.Status.Should().Be(VerificationStatus.FakeLossless);
    }

    [Fact]
    public void IncompleteLogMatch_DoesNotUpgradeStatus()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0) with
        {
            Log = new RipLogVerification
            {
                LogPath = "album.log",
                LogTrackCount = 2,
                ComparedTracks = 1,
                MatchedTracks = 1
            }
        };
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };

        var result = _classifier.Classify(partial, spectrum, OriginSignals.Empty);

        result.Status.Should().Be(VerificationStatus.UndeterminedLossless);
    }

    private static RipLogVerification VerifiedLog(bool isVirtual)
    {
        return new RipLogVerification
        {
            LogPath = "album.log",
            DriveName = isVirtual ? "DiscSoftVirtual" : "HL-DT-STBD-RE WH14NS40",
            IsVirtualDrive = isVirtual,
            LogTrackCount = 1,
            ComparedTracks = 1,
            MatchedTracks = 1
        };
    }

    [Fact]
    public void CA3_T16_AccurateRipMissing_ButCtdbAccurate_VerifiedCdRip()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0) with
        {
            CtdbStatus = CtdbStatus.AccuratelyRipped,
            CtdbConfidenceScore = 11,
            CtdbMatches = 11
        };
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };

        var result = _classifier.Classify(partial, spectrum, OriginSignals.Empty);

        result.AccurateRipStatus.Should().Be(AccurateRipStatus.NotFound);
        result.Status.Should().Be(VerificationStatus.VerifiedCdRip);
        result.Diagnostics.Should().Contain(d => d.Contains("CTDB accurate"));
    }

    [Fact]
    public void CA3_T17_AccurateRipMissing_ButCtdbAccurate_SpectrumEmpty_VerifiedCdRip()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0) with
        {
            CtdbStatus = CtdbStatus.AccuratelyRipped,
            CtdbConfidenceScore = 5,
            CtdbMatches = 5
        };

        var result = _classifier.Classify(partial, SpectrumAnalysis.Empty, OriginSignals.Empty);

        result.AccurateRipStatus.Should().Be(AccurateRipStatus.NotFound);
        result.Status.Should().Be(VerificationStatus.VerifiedCdRip);
        result.Diagnostics.Should().Contain(d => d.Contains("Classifier_SpectrumUnavailable"));
    }

    [Fact]
    public void ArInaccurate_ButCtdbAccurate_VerifiedWithWarnings()
    {
        var partial = PartialResult(AccurateRipStatus.Inaccurate, 0) with
        {
            CtdbStatus = CtdbStatus.AccuratelyRipped,
            CtdbConfidenceScore = 11,
            CtdbMatches = 11
        };
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };

        var result = _classifier.Classify(partial, spectrum, OriginSignals.Empty);

        result.Status.Should().Be(VerificationStatus.VerifiedWithWarnings);
        result.Diagnostics.Should().Contain("Classifier_CtdbVerifiedWithArMismatch");
    }

    [Fact]
    public void ArInaccurate_ButCtdbAccurate_SpectrumEmpty_VerifiedWithWarnings()
    {
        var partial = PartialResult(AccurateRipStatus.Inaccurate, 0) with
        {
            CtdbStatus = CtdbStatus.AccuratelyRipped,
            CtdbConfidenceScore = 5,
            CtdbMatches = 5
        };

        var result = _classifier.Classify(partial, SpectrumAnalysis.Empty, OriginSignals.Empty);

        result.Status.Should().Be(VerificationStatus.VerifiedWithWarnings);
        result.Diagnostics.Should().Contain("Classifier_CtdbVerifiedWithArMismatch");
    }

    [Fact]
    public void DbPowerampAccurate_ButArInaccurate_VerifiedWithWarnings()
    {
        var partial = PartialResult(AccurateRipStatus.Inaccurate, 0) with
        {
            DbPoweramp = DbAccurate()
        };
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };

        var result = _classifier.Classify(partial, spectrum, OriginSignals.Empty);

        result.Status.Should().Be(VerificationStatus.VerifiedWithWarnings);
        result.Diagnostics.Should().Contain("Classifier_DbPowerampAccurateWithArMismatch");
    }

    [Fact]
    public void DbPowerampAccurate_WithLiveDatabasesMissing_VerifiedCdRip()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0) with
        {
            CtdbStatus = CtdbStatus.NotFound,
            DbPoweramp = DbAccurate()
        };
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };

        var result = _classifier.Classify(partial, spectrum, OriginSignals.Empty);

        result.Status.Should().Be(VerificationStatus.VerifiedCdRip);
    }

    [Fact]
    public void DbPowerampAccurate_ButBrickwall_FakeLossless()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0) with
        {
            CtdbStatus = CtdbStatus.NotFound,
            DbPoweramp = DbAccurate()
        };
        var spectrum = SpectrumAnalysis.Empty with
        {
            BandEnergyDb = new List<double> { -10, -20 },
            IsBrickwallSuspected = true,
            BrickwallScore = 0.9,
            NyquistCutoffHz = 16000
        };

        var result = _classifier.Classify(partial, spectrum, OriginSignals.Empty);

        result.Status.Should().Be(VerificationStatus.FakeLossless);
    }

    private static DbPowerampEvidence DbAccurate()
    {
        return new DbPowerampEvidence
        {
            AccurateRipDiscId = "00142e60-00bd0bfe-950acb0c",
            AccurateRipResult = "Accurate (confidence 12)",
            IsAccurate = true
        };
    }

    [Fact]
    public void CA3_T10_AccuratelyRipped_Plus_SpectrumEmpty_VerifiedCdRip_WithWarning()
    {
        var partial = PartialResult(AccurateRipStatus.AccuratelyRipped, 5);
        var spectrum = SpectrumAnalysis.Empty;
        var origin = OriginSignals.Empty;

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.VerifiedCdRip);
        result.StatusIsPreliminary.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Contains("Classifier_SpectrumUnavailable"));
    }

    [Fact]
    public void CA3_T11_NotFound_Plus_SpectrumEmpty_UndeterminedLossless_WithWarning()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0);
        var spectrum = SpectrumAnalysis.Empty;
        var origin = OriginSignals.Empty;

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Status.Should().Be(VerificationStatus.UndeterminedLossless);
        result.StatusIsPreliminary.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Contains("Classifier_SpectrumUnavailable"));
    }

    [Fact]
    public void CA3_T12_DiagnosticsContainsRuleLines()
    {
        var partial = PartialResult(AccurateRipStatus.NotFound, 0);
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10, -20 } };
        var origin = OriginSignals.Empty with { StreamingConfidence = 0.8 };

        var result = _classifier.Classify(partial, spectrum, origin);

        result.Diagnostics.Should().Contain(d => d.StartsWith("Classifier:"));
    }

    [Fact]
    public void CA3_T13_StatusIsPreliminaryFalse_InAllCases()
    {
        var cases = new[]
        {
            PartialResult(AccurateRipStatus.AccuratelyRipped, 1),
            PartialResult(AccurateRipStatus.NotFound, 0),
            PartialResult(AccurateRipStatus.Inaccurate, 0),
            PartialResult(AccurateRipStatus.Unprocessable, 0)
        };

        foreach (var partial in cases)
        {
            var result = _classifier.Classify(partial, SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -1 } }, OriginSignals.Empty);
            result.StatusIsPreliminary.Should().BeFalse();
        }
    }

    [Fact]
    public void CA3_T14_ClassifierOnlyUpdatesStatusStatusIsPreliminaryDiagnostics()
    {
        var partial = PartialResult(AccurateRipStatus.AccuratelyRipped, 7);
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10 } };
        var origin = OriginSignals.Empty;

        var result = _classifier.Classify(partial, spectrum, origin);

        result.AlbumPath.Should().Be(partial.AlbumPath);
        result.AccurateRipStatus.Should().Be(partial.AccurateRipStatus);
        result.ConfidenceScore.Should().Be(partial.ConfidenceScore);
        result.DriveOffset.Should().Be(partial.DriveOffset);
        result.CtdbParityAvailable.Should().Be(partial.CtdbParityAvailable);
        result.Status.Should().NotBe(partial.Status);
        result.StatusIsPreliminary.Should().BeFalse();
    }

    [Fact]
    public void CA3_T15_ClassifierIsPure_NoIO_NoLogging()
    {
        // Verified by inspection: Classify uses no I/O, no logger, no static state
        var partial = PartialResult(AccurateRipStatus.AccuratelyRipped, 3);
        var spectrum = SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10 } };
        var origin = OriginSignals.Empty;

        var result1 = _classifier.Classify(partial, spectrum, origin);
        var result2 = _classifier.Classify(partial, spectrum, origin);

        result1.Should().BeEquivalentTo(result2); // deterministic, pure
    }
}
