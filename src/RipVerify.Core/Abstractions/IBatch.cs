namespace RipVerify.Core.Abstractions;

using RipVerify.Core.Models;

public interface IVerificationClassifier
{
    VerificationResult Classify(
        VerificationResult partialResult,
        SpectrumAnalysis spectrum,
        OriginSignals origin);
}

public interface IBatchProcessor
{
    Task RunAsync(
        IReadOnlyList<ScanInput> inputs,
        IProgress<BatchProgress> progress,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, CancellationToken>? itemCancellationTokens = null);
}
