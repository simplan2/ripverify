// Validated against CUETools 2.2.6
// Parser is versioned and defensive - handles unknown lines without throwing

using System.Text.RegularExpressions;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;

namespace RipVerify.Infrastructure.Cue;

public sealed class CueToolsOutputParser
{
    private enum Section
    {
        None,
        CtdbTracks,
        AccurateRipTracks,
        AccurateRipOffsets
    }

    private static readonly char[] LineSeparators = new[] { '\r', '\n' };
    private static readonly Regex CtdbTrackRegex = new(
        @"^\s*(\d+)\s*\|\s*\(\s*(\d+)\s*/\s*(\d+)\s*\)\s*(.*)$",
        RegexOptions.Compiled);
    private static readonly Regex AccurateRipTrackRegex = new(
        @"^\s*(\d+)\s+\[[^\]]+\]\s+\((\d+)\+(\d+)/(\d+)\)\s*(.*)$",
        RegexOptions.Compiled);
    private static readonly Regex DriveOffsetRegex = new(
        @"(?:Drive\s+offset|Offset)[:\s]+([+-]?\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ConfidenceRegex = new(
        @"confidence[:\s]+(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static VerificationResult Parse(CueToolsRunResult runResult, CueSheet cueSheet, AppSettings settings)
    {
        var parserWarnings = new List<string>();
        var diagnostics = new List<string>();
        var process = runResult.Process;

        var fullOutput = process.StandardOutput;
        if (!string.IsNullOrEmpty(process.StandardError))
        {
            fullOutput = string.IsNullOrEmpty(fullOutput) ? process.StandardError : fullOutput + "\n" + process.StandardError;
        }

        var lines = fullOutput.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);

        if (process.WasCancelled)
        {
            return CreateUnprocessable(cueSheet, settings, diagnostics, parserWarnings, "Parser_Cancelled");
        }

        if (process.TimedOut)
        {
            return CreateUnprocessable(cueSheet, settings, diagnostics, parserWarnings, "Parser_TimedOut");
        }

        if (process.ExitCode != 0)
        {
            var detail = FirstMeaningfulLine(lines);
            var diagnostic = detail is null
                ? $"Parser_ExitCodeNonZero:{process.ExitCode}"
                : $"Parser_ExitCodeNonZero:{process.ExitCode}:{detail}";
            return CreateUnprocessable(cueSheet, settings, diagnostics, parserWarnings, diagnostic);
        }

        var section = Section.None;
        var accurateRipFound = false;
        var accurateRipMissing = false;
        var ctdbFound = false;
        var ctdbMissing = false;
        var accurateRipMatches = 0;
        var accurateRipMismatches = 0;
        var accurateRipTrackCount = 0;
        var accurateRipConfidence = 0;
        var ctdbMatches = 0;
        var ctdbMismatches = 0;
        var ctdbTrackCount = 0;
        var ctdbConfidence = 0;
        int? driveOffset = null;

        foreach (var rawLine in lines)
        {
            try
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.Contains("CTDB TOCID:", StringComparison.OrdinalIgnoreCase))
                {
                    if (line.Contains("disk not present", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    {
                        ctdbMissing = true;
                    }
                    else if (line.Contains("found", StringComparison.OrdinalIgnoreCase))
                    {
                        ctdbFound = true;
                    }

                    section = Section.None;
                    continue;
                }

                if (line.Contains("AccurateRip ID:", StringComparison.OrdinalIgnoreCase))
                {
                    if (line.Contains("disk not present", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    {
                        accurateRipMissing = true;
                    }
                    else if (line.Contains("found", StringComparison.OrdinalIgnoreCase))
                    {
                        accurateRipFound = true;
                    }

                    section = Section.None;
                    continue;
                }

                if (line.StartsWith("Offsetted by", StringComparison.OrdinalIgnoreCase))
                {
                    section = Section.AccurateRipOffsets;
                    continue;
                }

                if (line.StartsWith("Track | CTDB Status", StringComparison.OrdinalIgnoreCase))
                {
                    section = Section.CtdbTracks;
                    continue;
                }

                if (line.StartsWith("Track", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("CRC", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("Status", StringComparison.OrdinalIgnoreCase))
                {
                    section = Section.AccurateRipTracks;
                    continue;
                }

                if (line.StartsWith("Track", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("Peak", StringComparison.OrdinalIgnoreCase))
                {
                    section = Section.None;
                    continue;
                }

                var confidenceMatch = ConfidenceRegex.Match(line);
                if (confidenceMatch.Success && int.TryParse(confidenceMatch.Groups[1].Value, out var explicitConfidence))
                {
                    accurateRipConfidence = Math.Max(accurateRipConfidence, explicitConfidence);
                }

                var offsetMatch = DriveOffsetRegex.Match(line);
                if (offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, out var offset))
                {
                    driveOffset = offset;
                }

                if (section == Section.CtdbTracks)
                {
                    var match = CtdbTrackRegex.Match(line);
                    if (match.Success)
                    {
                        ctdbTrackCount++;
                        if (int.TryParse(match.Groups[2].Value, out var count))
                        {
                            ctdbConfidence = Math.Max(ctdbConfidence, count);
                        }

                        if (IsAccurate(match.Groups[4].Value))
                        {
                            ctdbMatches++;
                        }
                        else
                        {
                            ctdbMismatches++;
                            AddParserWarning(parserWarnings, line);
                        }
                    }

                    continue;
                }

                if (section == Section.AccurateRipTracks)
                {
                    var match = AccurateRipTrackRegex.Match(line);
                    if (match.Success)
                    {
                        accurateRipTrackCount++;
                        var versionOne = ParseInt(match.Groups[2].Value);
                        var versionTwo = ParseInt(match.Groups[3].Value);
                        if (IsAccurate(match.Groups[5].Value))
                        {
                            accurateRipMatches++;
                            accurateRipConfidence = Math.Max(accurateRipConfidence, versionOne + versionTwo);
                        }
                        else
                        {
                            accurateRipMismatches++;
                            AddParserWarning(parserWarnings, line);
                        }
                    }

                    continue;
                }

                var lowerLine = line.ToLowerInvariant();
                if (lowerLine.Contains("error") || lowerLine.Contains("warning") || lowerLine.Contains("fail") ||
                    lowerLine.Contains("unknown") || lowerLine.Contains("unrecognized") || lowerLine.Contains("exception") ||
                    lowerLine.Contains("differs"))
                {
                    AddParserWarning(parserWarnings, line);
                }
            }
            catch
            {
                // Never throw - just continue
                continue;
            }
        }

        var accurateRipStatus = AccurateRipStatus.Unprocessable;
        var ctdbStatus = CtdbStatus.Unprocessable;
        var confidenceScore = 0;

        if (accurateRipFound && accurateRipTrackCount > 0)
        {
            confidenceScore = accurateRipConfidence;
            accurateRipStatus = accurateRipMismatches == 0
                ? AccurateRipStatus.AccuratelyRipped
                : AccurateRipStatus.Inaccurate;
        }
        else if (accurateRipMissing)
        {
            accurateRipStatus = AccurateRipStatus.NotFound;
        }

        if (ctdbFound && ctdbTrackCount > 0)
        {
            ctdbStatus = ctdbMismatches == 0
                ? CtdbStatus.AccuratelyRipped
                : CtdbStatus.Inaccurate;
        }
        else if (ctdbMissing)
        {
            ctdbStatus = CtdbStatus.NotFound;
        }

        if (accurateRipStatus == AccurateRipStatus.NotFound && ctdbStatus == CtdbStatus.AccuratelyRipped)
        {
            confidenceScore = ctdbConfidence;
        }

        if (accurateRipStatus == AccurateRipStatus.Unprocessable &&
            ctdbStatus == CtdbStatus.Unprocessable)
        {
            diagnostics.Add("Parser_StatusNotDetected");
        }
        else
        {
            diagnostics.Add(
                $"Parser_Status:{accurateRipStatus};ARFound={accurateRipFound};ARMissing={accurateRipMissing};" +
                $"ARTracks={accurateRipTrackCount};ARMatches={accurateRipMatches};ARMismatches={accurateRipMismatches};" +
                $"ARConfidence={accurateRipConfidence};CTDBStatus={ctdbStatus};CTDBFound={ctdbFound};" +
                $"CTDBTracks={ctdbTrackCount};CTDBMatches={ctdbMatches};CTDBMismatches={ctdbMismatches};" +
                $"CTDBConfidence={ctdbConfidence}");
        }

        // The parser only reports AccurateRip status. CTDB is reported separately so the
        // classifier can validate a rip when AccurateRip has no entry.
        var preliminaryStatus = accurateRipStatus switch
        {
            AccurateRipStatus.AccuratelyRipped => VerificationStatus.VerifiedCdRip,
            AccurateRipStatus.Inaccurate => VerificationStatus.FakeLossless,
            AccurateRipStatus.NotFound => VerificationStatus.UndeterminedLossless,
            _ => VerificationStatus.Unprocessable
        };

        var finalDriveOffset = driveOffset ?? settings.DriveOffset;

        return new VerificationResult
        {
            AlbumPath = cueSheet.FilePath ?? string.Empty,
            CuePath = cueSheet.FilePath,
            CueOrigin = cueSheet.Origin,
            AccurateRipStatus = accurateRipStatus,
            ConfidenceScore = confidenceScore,
            CtdbStatus = ctdbStatus,
            CtdbConfidenceScore = ctdbConfidence,
            CtdbMatches = ctdbMatches,
            CtdbMismatches = ctdbMismatches,
            DriveOffset = finalDriveOffset,
            CtdbParityAvailable = ctdbMatches > 0,
            Spectrum = SpectrumAnalysis.Empty,
            Origin = OriginSignals.Empty,
            Status = preliminaryStatus,
            Diagnostics = diagnostics.Take(20).ToList(),
            VerifiedAt = DateTimeOffset.UtcNow,
            StatusIsPreliminary = true,
            ParserWarnings = parserWarnings.Take(20).ToList()
        };
    }

    private static bool IsAccurate(string status)
    {
        return status.Contains("accurately ripped", StringComparison.OrdinalIgnoreCase) ||
               (status.Contains("accurate", StringComparison.OrdinalIgnoreCase) &&
                !status.Contains("no match", StringComparison.OrdinalIgnoreCase) &&
                !status.Contains("differs", StringComparison.OrdinalIgnoreCase));
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static void AddParserWarning(List<string> parserWarnings, string line)
    {
        if (parserWarnings.Count < 20)
        {
            parserWarnings.Add(line);
        }
    }

    private static string? FirstMeaningfulLine(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("[CUETools log", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return trimmed.Length > 160 ? trimmed[..160] : trimmed;
        }

        return null;
    }

    private static VerificationResult CreateUnprocessable(
        CueSheet cueSheet,
        AppSettings settings,
        List<string> diagnostics,
        List<string> parserWarnings,
        string diagnostic)
    {
        diagnostics.Add(diagnostic);
        return new VerificationResult
        {
            AlbumPath = cueSheet.FilePath ?? string.Empty,
            CuePath = cueSheet.FilePath,
            CueOrigin = cueSheet.Origin,
            AccurateRipStatus = AccurateRipStatus.Unprocessable,
            ConfidenceScore = 0,
            CtdbStatus = CtdbStatus.Unprocessable,
            DriveOffset = settings.DriveOffset,
            CtdbParityAvailable = false,
            Spectrum = SpectrumAnalysis.Empty,
            Origin = OriginSignals.Empty,
            Status = VerificationStatus.Unprocessable,
            Diagnostics = diagnostics,
            VerifiedAt = DateTimeOffset.UtcNow,
            StatusIsPreliminary = true,
            ParserWarnings = parserWarnings
        };
    }
}
