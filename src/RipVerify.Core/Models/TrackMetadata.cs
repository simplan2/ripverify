namespace RipVerify.Core.Models;

public sealed record TrackMetadata
{
    public int Number { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Isrc { get; init; }
    public TimeSpan Duration { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public bool IsIncomplete { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
