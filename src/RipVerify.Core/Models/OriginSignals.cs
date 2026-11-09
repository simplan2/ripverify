namespace RipVerify.Core.Models;

public sealed record OriginSignals
{
    public bool HasPlatformIsrc { get; init; }
    public string? PlatformIsrcExample { get; init; }
    public string? DigitalDistributor { get; init; }
    public bool HasStreamingOnlyTags { get; init; }
    public double StreamingConfidence { get; init; }
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
    public static OriginSignals Empty { get; } = new();
}
