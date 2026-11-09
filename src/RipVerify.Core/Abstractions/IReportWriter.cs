using RipVerify.Core.Models;

namespace RipVerify.Core.Abstractions;

public interface IReportWriter
{
    Task WriteJsonAsync(
        string albumFolder,
        ReportDocument document,
        CancellationToken cancellationToken);

    Task WriteMarkdownAsync(
        string albumFolder,
        ReportDocument document,
        bool forceEnglish,
        CancellationToken cancellationToken);
}

public interface IReportDocumentBuilder
{
    ReportDocument Build(
        AlbumMetadata album,
        CueSheet cue,
        VerificationResult result,
        IReadOnlyList<string>? cueWarnings = null);
}
