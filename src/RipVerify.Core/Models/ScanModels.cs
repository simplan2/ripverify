namespace RipVerify.Core.Models;

public enum ScanInputKind
{
    AlbumFolder,
    LooseFiles,
    SingleImageWithCue,
    SingleImageWithoutCue,
    Unknown
}

public sealed record ScanInput
{
    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();
}

public sealed record ScanResult
{
    public ScanInputKind Kind { get; init; }
    public string? AlbumFolder { get; init; }
    public string? CuePath { get; init; }
    public IReadOnlyList<string> AudioFilePaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LooseFilePaths { get; init; } = Array.Empty<string>();
    public AlbumMetadata? Album { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

public sealed record CueFixResult
{
    public CueSheet? Cue { get; init; }
    public bool WasRepaired { get; init; }
    public bool WasGenerated { get; init; }
    public CueOrigin Origin { get; init; } = CueOrigin.Original;
    public IReadOnlyList<string> RepairNotes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}
