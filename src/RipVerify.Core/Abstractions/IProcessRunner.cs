using RipVerify.Core.Models;

namespace RipVerify.Core.Abstractions;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        Action<string>? onStandardOutputLine = null,
        Action<string>? onStandardErrorLine = null,
        TimeSpan? timeout = null);
}
