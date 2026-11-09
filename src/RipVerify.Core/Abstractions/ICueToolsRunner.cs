using RipVerify.Core.Models;

namespace RipVerify.Core.Abstractions;

public interface ICueToolsRunner
{
    Task<CueToolsRunResult> RunAsync(
        CueSheet cue,
        string audioDirectory,
        CancellationToken cancellationToken);
}
