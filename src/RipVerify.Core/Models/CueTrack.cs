namespace RipVerify.Core.Models;

public sealed record CueTrack
{
    public int Number { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public TimeSpan Start { get; init; }
    public TimeSpan? Pregap { get; init; }
}
