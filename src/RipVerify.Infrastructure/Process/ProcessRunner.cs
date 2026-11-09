using System.Diagnostics;
using System.Text;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;

namespace RipVerify.Infrastructure.Process;

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

    public async Task<ProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken,
        Action<string>? onStandardOutputLine = null,
        Action<string>? onStandardErrorLine = null,
        TimeSpan? timeout = null)
    {
        var timeoutToUse = timeout ?? DefaultTimeout;
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StandardError = "Executable path is empty.",
                Duration = stopwatch.Elapsed
            };
        }

        if (!File.Exists(executablePath))
        {
            return new ProcessResult
            {
                ExitCode = -1,
                StandardError = $"Executable not found: {executablePath}",
                Duration = stopwatch.Elapsed
            };
        }

        using var process = new System.Diagnostics.Process();
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in arguments ?? Array.Empty<string>())
        {
            startInfo.ArgumentList.Add(arg);
        }

        process.StartInfo = startInfo;

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        try
        {
            if (!process.Start())
            {
                return new ProcessResult
                {
                    ExitCode = -1,
                    StandardError = "Failed to start process.",
                    Duration = stopwatch.Elapsed
                };
            }

            var outputTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) != null)
                {
                    outputBuilder.AppendLine(line);
                    try
                    {
                        onStandardOutputLine?.Invoke(line);
                    }
                    catch
                    {
                    }
                }
            }, cancellationToken);

            var errorTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await process.StandardError.ReadLineAsync(cancellationToken)) != null)
                {
                    errorBuilder.AppendLine(line);
                    try
                    {
                        onStandardErrorLine?.Invoke(line);
                    }
                    catch
                    {
                    }
                }
            }, cancellationToken);

            var waitTask = process.WaitForExitAsync(cancellationToken);
            var completedTask = await Task.WhenAny(waitTask, Task.Delay(timeoutToUse, cancellationToken));

            if (completedTask == waitTask)
            {
                await Task.WhenAll(outputTask, errorTask);
                stopwatch.Stop();
                return new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = outputBuilder.ToString(),
                    StandardError = errorBuilder.ToString(),
                    Duration = stopwatch.Elapsed,
                    WasCancelled = false,
                    TimedOut = false
                };
            }

            // Timed out or cancelled
            if (cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }

                try
                {
                    await Task.WhenAll(outputTask, errorTask);
                }
                catch
                {
                }

                stopwatch.Stop();
                return new ProcessResult
                {
                    ExitCode = -1,
                    StandardOutput = outputBuilder.ToString(),
                    StandardError = errorBuilder.ToString(),
                    Duration = stopwatch.Elapsed,
                    WasCancelled = true,
                    TimedOut = false
                };
            }
            else
            {
                // Timeout
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }

                try
                {
                    await Task.WhenAll(outputTask, errorTask);
                }
                catch
                {
                }

                stopwatch.Stop();
                return new ProcessResult
                {
                    ExitCode = -1,
                    StandardOutput = outputBuilder.ToString(),
                    StandardError = errorBuilder.ToString(),
                    Duration = stopwatch.Elapsed,
                    WasCancelled = false,
                    TimedOut = true
                };
            }
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            stopwatch.Stop();
            return new ProcessResult
            {
                ExitCode = -1,
                StandardOutput = outputBuilder.ToString(),
                StandardError = errorBuilder.ToString(),
                Duration = stopwatch.Elapsed,
                WasCancelled = true,
                TimedOut = false
            };
        }
        catch (Exception ex)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            stopwatch.Stop();
            return new ProcessResult
            {
                ExitCode = -1,
                StandardOutput = outputBuilder.ToString(),
                StandardError = ex.Message,
                Duration = stopwatch.Elapsed,
                WasCancelled = false,
                TimedOut = false
            };
        }
    }
}
