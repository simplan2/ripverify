using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;

namespace RipVerify.Infrastructure.Reporting;

public sealed class ReportDocumentBuilder : IReportDocumentBuilder
{
    private static readonly string AppVersion =
        typeof(ReportDocumentBuilder).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public ReportDocument Build(
        AlbumMetadata album,
        CueSheet cue,
        VerificationResult result,
        IReadOnlyList<string>? cueWarnings = null)
    {
        var albumPath = string.IsNullOrWhiteSpace(result.AlbumPath)
            ? Path.GetDirectoryName(cue.FilePath) ?? string.Empty
            : result.AlbumPath;

        return new ReportDocument
        {
            SchemaVersion = ReportWriter.CurrentSchemaVersion,
            App = "RipVerify",
            AppVersion = AppVersion,
            VerifiedAt = result.VerifiedAt == default ? DateTimeOffset.UtcNow : result.VerifiedAt,
            AlbumPath = albumPath,
            Album = album,
            Results = new[]
            {
                new ReportResultEntry
                {
                    AlbumPath = result.AlbumPath,
                    CuePath = result.CuePath ?? cue.FilePath,
                    CueOrigin = result.CueOrigin,
                    DbPoweramp = result.DbPoweramp,
                    AccurateRipStatus = result.AccurateRipStatus,
                    ConfidenceScore = result.ConfidenceScore,
                    CtdbStatus = result.CtdbStatus,
                    CtdbConfidenceScore = result.CtdbConfidenceScore,
                    DriveOffset = result.DriveOffset,
                    CtdbParityAvailable = result.CtdbParityAvailable,
                    Spectrum = result.Spectrum,
                    Origin = result.Origin,
                    Log = result.Log,
                    CueWarnings = cueWarnings ?? Array.Empty<string>(),
                    ParserWarnings = result.ParserWarnings,
                    Status = result.Status,
                    Diagnostics = result.Diagnostics
                }
            }
        };
    }
}
