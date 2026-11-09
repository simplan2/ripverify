namespace RipVerify.Core.Models;

public sealed record DbPowerampTrackEvidence
{
    public int TrackNumber { get; init; }
    public string AccurateRipResult { get; init; } = string.Empty;
    public bool IsAccurate { get; init; }
}

public sealed record DbPowerampEvidence
{
    public static DbPowerampEvidence Empty { get; } = new();

    public string? AccurateRipDiscId { get; init; }
    public string? AccurateRipResult { get; init; }
    public bool IsAccurate { get; init; }
    public IReadOnlyList<DbPowerampTrackEvidence> Tracks { get; init; } = Array.Empty<DbPowerampTrackEvidence>();
}
