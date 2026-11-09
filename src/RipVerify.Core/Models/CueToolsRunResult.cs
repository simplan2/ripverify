namespace RipVerify.Core.Models;

public sealed record CueToolsRunResult
{
    public ProcessResult Process { get; init; } = new();
    public string CommandLine { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public bool Succeeded => Process.ExitCode == 0 && !Process.WasCancelled && !Process.TimedOut;
}
