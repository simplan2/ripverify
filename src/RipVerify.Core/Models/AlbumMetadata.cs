namespace RipVerify.Core.Models;

public sealed record AlbumMetadata
{
    public string Album { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string? MusicBrainzReleaseId { get; init; }
    public IReadOnlyList<TrackMetadata> Tracks { get; init; } = Array.Empty<TrackMetadata>();
    public bool MetadataFallbackUsed { get; init; }
    public DbPowerampEvidence DbPoweramp { get; init; } = DbPowerampEvidence.Empty;
}
