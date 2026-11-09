namespace RipVerify.Core.Models;

public sealed record ProcessResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public bool WasCancelled { get; init; }
    public bool TimedOut { get; init; }
}
