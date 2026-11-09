namespace RipVerify.Core.Abstractions;

using RipVerify.Core.Models;

public interface IFolderScanner
{
    Task<ScanResult> ScanAsync(ScanInput input, CancellationToken cancellationToken);
}

public interface ICueFixerService
{
    Task<CueFixResult> FixAsync(ScanResult scan, CancellationToken cancellationToken);
}

public interface ICueSanitizer
{
    Task<CueSheet> SanitizeAsync(string cuePath, CancellationToken cancellationToken);
}

public interface ICueGenerator
{
    Task<CueSheet?> GenerateAsync(AlbumMetadata metadata, CancellationToken cancellationToken);
}

public interface ICueMetadataReader
{
    Task<AlbumMetadata> ReadAlbumMetadataAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken);
}

public interface IMusicBrainzClient
{
    Task<AlbumMetadata?> FindReleaseByTocAsync(
        string toc,
        CancellationToken cancellationToken);
    Task<AlbumMetadata?> FindReleaseByDiscIdAsync(string discId, CancellationToken cancellationToken);
    Task<AlbumMetadata?> GetReleaseAsync(string releaseId, CancellationToken cancellationToken);
}

public interface ISpectrumAnalyzer
{
    Task<SpectrumAnalysis> AnalyzeAsync(
        string audioFilePath,
        SpectrumAnalysisOptions options,
        CancellationToken cancellationToken);

    /// <summary>
    /// Analyzes every track of the album and aggregates the results using the worst case:
    /// BrickwallScore is the maximum across tracks, NyquistCutoffHz the minimum, and
    /// WorstTrackFilePath points to the track that triggered the worst analysis.
    /// </summary>
    Task<SpectrumAnalysis> AnalyzeAlbumAsync(
        AlbumMetadata album,
        SpectrumAnalysisOptions options,
        CancellationToken cancellationToken);
}

public interface IOriginSignalReader
{
    Task<OriginSignals> ReadAsync(
        AlbumMetadata album,
        CancellationToken cancellationToken);
}

public interface ICalibrationCorpus
{
    Task<CalibrationCorpus> LoadAsync(CancellationToken cancellationToken);
    Task SaveEntryAsync(CalibrationEntry entry, CancellationToken cancellationToken);
}
