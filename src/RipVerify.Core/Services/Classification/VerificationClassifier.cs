using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;

namespace RipVerify.Core.Services.Classification;

public sealed class VerificationClassifier : RipVerify.Core.Abstractions.IVerificationClassifier
{
    public static VerificationResult ClassifyStatic(VerificationResult partialResult, SpectrumAnalysis spectrum, OriginSignals origin)
    {
        return ClassifyInternal(partialResult, spectrum, origin);
    }

    public VerificationResult Classify(VerificationResult partialResult, SpectrumAnalysis spectrum, OriginSignals origin)
    {
        return ClassifyInternal(partialResult, spectrum, origin);
    }

    private static VerificationResult ClassifyInternal(VerificationResult partialResult, SpectrumAnalysis spectrum, OriginSignals origin)
    {
        var diagnostics = new List<string>(partialResult.Diagnostics ?? Array.Empty<string>());
        spectrum ??= SpectrumAnalysis.Empty;
        origin ??= OriginSignals.Empty;

        bool spectrumUnavailable = ReferenceEquals(spectrum, SpectrumAnalysis.Empty);
        if (spectrumUnavailable)
        {
            diagnostics.Add("Classifier_SpectrumUnavailable");
        }

        bool highCutoffMasteringFilter = spectrum.NyquistCutoffHz > SpectrumThresholds.HighCutoffGuardHz;
        if (spectrum.IsBrickwallSuspected && highCutoffMasteringFilter)
        {
            diagnostics.Add("Classifier_HighCutoffMasteringFilter");
        }

        VerificationStatus finalStatus;
        AccurateRipStatus arStatus = partialResult.AccurateRipStatus;
        CtdbStatus ctdbStatus = partialResult.CtdbStatus;

        bool arVerified = arStatus == AccurateRipStatus.AccuratelyRipped && partialResult.ConfidenceScore > 0;
        bool ctdbVerified = ctdbStatus == CtdbStatus.AccuratelyRipped &&
                            (partialResult.CtdbConfidenceScore > 0 || partialResult.CtdbMatches > 0);
        bool databaseVerified = arVerified || ctdbVerified;
        bool dbPowerampAccurate = partialResult.DbPoweramp?.IsAccurate == true;
        bool ctdbConflict = arStatus == AccurateRipStatus.Inaccurate && ctdbVerified;
        bool tagConflict = dbPowerampAccurate &&
                           (arStatus == AccurateRipStatus.Inaccurate || ctdbStatus == CtdbStatus.Inaccurate);
        bool sourceKnown = arStatus != AccurateRipStatus.Unprocessable ||
                           ctdbStatus != CtdbStatus.Unprocessable ||
                           dbPowerampAccurate;
        bool sourceFailed = arStatus is AccurateRipStatus.NotFound or AccurateRipStatus.Inaccurate ||
                            ctdbStatus is CtdbStatus.NotFound or CtdbStatus.Inaccurate;
        bool logVerified = partialResult.Log?.AllTracksMatched == true;
        bool logVirtualDrive = partialResult.Log?.IsVirtualDrive == true;

        // Priority 1: no database produced usable information
        if (!sourceKnown)
        {
            diagnostics.Add("Classifier: unprocessable (status=Unprocessable)");
            finalStatus = VerificationStatus.Unprocessable;
        }
        // Spectrum unavailable: cannot confirm brickwall, fall back to database and log evidence
        else if (spectrumUnavailable)
        {
            if (ctdbConflict || tagConflict)
            {
                AddConflictDiagnostics(diagnostics, ctdbConflict, tagConflict);
                finalStatus = VerificationStatus.VerifiedWithWarnings;
            }
            else if (dbPowerampAccurate)
            {
                diagnostics.Add("Classifier: dBpoweramp tag accurate with unavailable spectrum (status=VerifiedCdRip)");
                finalStatus = VerificationStatus.VerifiedCdRip;
            }
            else if (databaseVerified)
            {
                diagnostics.Add(ctdbVerified && !arVerified
                    ? "Classifier: CTDB accurate with unavailable spectrum (status=VerifiedCdRip)"
                    : "Classifier: accurate rip with unavailable spectrum (status=VerifiedCdRip)");
                finalStatus = VerificationStatus.VerifiedCdRip;
            }
            else if (logVerified && logVirtualDrive)
            {
                diagnostics.Add("Classifier: log verified on virtual drive (status=UndeterminedLossless)");
                finalStatus = VerificationStatus.UndeterminedLossless;
            }
            else if (logVerified)
            {
                diagnostics.Add("Classifier: log CRC verified with unavailable spectrum (status=AuthenticRipWithLog)");
                finalStatus = VerificationStatus.AuthenticRipWithLog;
            }
            else if (sourceFailed)
            {
                diagnostics.Add("Classifier: undetermined due to unavailable spectrum (status=UndeterminedLossless)");
                finalStatus = VerificationStatus.UndeterminedLossless;
            }
            else
            {
                finalStatus = VerificationStatus.Unprocessable;
            }
        }
        // Priority 2: Brickwall -> FakeLossless, unless the cutoff is a mastering filter
        else if (spectrum.IsBrickwallSuspected && !highCutoffMasteringFilter)
        {
            diagnostics.Add("Classifier: brickwall detected (status=FakeLossless)");
            finalStatus = VerificationStatus.FakeLossless;
        }
        // Priority 3: conflicting database evidence -> VerifiedWithWarnings
        else if (ctdbConflict || tagConflict)
        {
            AddConflictDiagnostics(diagnostics, ctdbConflict, tagConflict);
            finalStatus = VerificationStatus.VerifiedWithWarnings;
        }
        // Priority 4: dBpoweramp file tag confirmed and no brickwall -> VerifiedCdRip
        else if (dbPowerampAccurate)
        {
            diagnostics.Add("Classifier: dBpoweramp tag accurate (status=VerifiedCdRip)");
            finalStatus = VerificationStatus.VerifiedCdRip;
        }
        // Priority 5: AccurateRip or CTDB confirmed and no brickwall -> VerifiedCdRip
        else if (databaseVerified)
        {
            diagnostics.Add(ctdbVerified && !arVerified
                ? "Classifier: CTDB accurate with no brickwall (status=VerifiedCdRip)"
                : "Classifier: accurate rip with no brickwall (status=VerifiedCdRip)");
            finalStatus = VerificationStatus.VerifiedCdRip;
        }
        // Priority 6: matching EAC/dBpoweramp log and physical drive
        else if (logVerified && logVirtualDrive)
        {
            diagnostics.Add("Classifier: log verified on virtual drive (status=UndeterminedLossless)");
            finalStatus = VerificationStatus.UndeterminedLossless;
        }
        else if (logVerified)
        {
            diagnostics.Add("Classifier: log CRC verified (status=AuthenticRipWithLog)");
            finalStatus = VerificationStatus.AuthenticRipWithLog;
        }
        // Priority 7: NotFound/Inaccurate + streaming confidence >= 0.5
        else if (sourceFailed && origin.StreamingConfidence >= 0.5)
        {
            diagnostics.Add("Classifier: origin indicates streaming (status=LikelyStreaming)");
            finalStatus = VerificationStatus.LikelyStreaming;
        }
        // Priority 8: NotFound/Inaccurate + low streaming confidence
        else if (sourceFailed)
        {
            diagnostics.Add("Classifier: undetermined origin (status=UndeterminedLossless)");
            finalStatus = VerificationStatus.UndeterminedLossless;
        }
        else
        {
            finalStatus = VerificationStatus.Unprocessable;
        }

        return partialResult with
        {
            Status = finalStatus,
            StatusIsPreliminary = false,
            Diagnostics = diagnostics,
            Spectrum = spectrum,
            Origin = origin
        };
    }

    private static void AddConflictDiagnostics(List<string> diagnostics, bool ctdbConflict, bool tagConflict)
    {
        if (ctdbConflict)
        {
            diagnostics.Add("Classifier_CtdbVerifiedWithArMismatch");
        }

        if (tagConflict)
        {
            diagnostics.Add("Classifier_DbPowerampAccurateWithArMismatch");
        }

        diagnostics.Add("Classifier: verified with warnings (status=VerifiedWithWarnings)");
    }
}
