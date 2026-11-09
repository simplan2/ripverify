namespace RipVerify.Core.Models;

public sealed record BatchItem
{
    public string InputPath { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public ScanResult? Scan { get; init; }
    public CueFixResult? CueFix { get; init; }
    public VerificationResult? Result { get; init; }
    public BatchItemState State { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public enum BatchItemState
{
    Pending,
    Scanning,
    FixingCue,
    VerifyingCueTools,
    AnalyzingSpectrum,
    ReadingOriginSignals,
    VerifyingLog,
    Classifying,
    Completed,
    Failed,
    Cancelled
}

public sealed record BatchProgress
{
    public int Total { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
    public int Cancelled { get; init; }
    public BatchItem? CurrentItem { get; init; }
    public IReadOnlyList<BatchItem> Items { get; init; } = Array.Empty<BatchItem>();
    public bool IsFinished { get; init; }
}
