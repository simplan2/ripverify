namespace RipVerify.Core.Models;

public sealed record CalibrationCorpus
{
    public IReadOnlyList<CalibrationEntry> Entries { get; init; } = Array.Empty<CalibrationEntry>();
    public double? CalibratedBrickwallThreshold { get; init; }
    public DateTimeOffset? LastCalibratedAt { get; init; }
}
