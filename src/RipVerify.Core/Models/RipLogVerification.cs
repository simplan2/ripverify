namespace RipVerify.Core.Models;

public sealed record RipLogTrack
{
    public int Number { get; init; }
    public string? FileName { get; init; }
    public string? TestCrc { get; init; }
    public string? CopyCrc { get; init; }
}

public sealed record RipLogDocument
{
    public string FilePath { get; init; } = string.Empty;
    public string? Creator { get; init; }
    public string? DriveName { get; init; }
    public bool IsVirtualDrive { get; init; }
    public IReadOnlyList<RipLogTrack> Tracks { get; init; } = Array.Empty<RipLogTrack>();
}

public sealed record RipLogVerification
{
    public string? LogPath { get; init; }
    public string? DriveName { get; init; }
    public bool IsVirtualDrive { get; init; }
    public int LogTrackCount { get; init; }
    public int ExpectedTrackCount { get; init; }
    public int ComparedTracks { get; init; }
    public int MatchedTracks { get; init; }
    public IReadOnlyList<int> MismatchedTrackNumbers { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public bool HasLog => !string.IsNullOrWhiteSpace(LogPath);

    public bool AllTracksMatched =>
        LogTrackCount > 0 &&
        (ExpectedTrackCount <= 0 || LogTrackCount == ExpectedTrackCount) &&
        ComparedTracks == LogTrackCount &&
        MatchedTracks == ComparedTracks &&
        MismatchedTrackNumbers.Count == 0;

    public static RipLogVerification Empty { get; } = new();
}
