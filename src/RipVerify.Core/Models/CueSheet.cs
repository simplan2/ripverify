namespace RipVerify.Core.Models;

public enum CueOrigin
{
    Original,
    Sanitized,
    GeneratedFromItoc,
    GeneratedFromMetadata,
    GeneratedFromTocLookup
}

public sealed record CueSheet
{
    public string FilePath { get; init; } = string.Empty;
    public string Encoding { get; init; } = "UTF-8";
    public string AlbumTitle { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public IReadOnlyList<CueTrack> Tracks { get; init; } = Array.Empty<CueTrack>();
    public bool WasGenerated { get; init; }
    public IReadOnlyList<string> RepairNotes { get; init; } = Array.Empty<string>();
    public CueOrigin Origin { get; init; } = CueOrigin.Original;
}
