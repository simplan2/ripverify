using System.Diagnostics;
using System.Text;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using Serilog;

namespace RipVerify.Infrastructure.Cue;

public sealed class CueToolsRunner : ICueToolsRunner
{
    private readonly IProcessRunner _processRunner;
    private readonly ISettingsService _settingsService;

    public CueToolsRunner(IProcessRunner processRunner, ISettingsService settingsService)
    {
        _processRunner = processRunner;
        _settingsService = settingsService;
    }

    public async Task<CueToolsRunResult> RunAsync(CueSheet cue, string audioDirectory, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var cueToolsPath = _settingsService.Current.CueToolsPath;

        if (string.IsNullOrWhiteSpace(cueToolsPath))
        {
            var process = new ProcessResult
            {
                ExitCode = -1,
                StandardError = "CUETools is not configured. Please set the path in Settings."
            };

            return new CueToolsRunResult
            {
                Process = process,
                CommandLine = string.Empty,
                StartedAt = startedAt
            };
        }

        if (!File.Exists(cueToolsPath))
        {
            var process = new ProcessResult
            {
                ExitCode = -1,
                StandardError = $"CUETools verifier not found at the configured path: {cueToolsPath}"
            };

            return new CueToolsRunResult
            {
                Process = process,
                CommandLine = string.Empty,
                StartedAt = startedAt
            };
        }

        if (cue.Tracks == null || cue.Tracks.Count == 0)
        {
            var process = new ProcessResult
            {
                ExitCode = -1,
                StandardError = "The CUE sheet has no tracks to verify."
            };

            return new CueToolsRunResult
            {
                Process = process,
                CommandLine = string.Empty,
                StartedAt = startedAt
            };
        }

        var cuePath = cue.FilePath;
        if (string.IsNullOrWhiteSpace(cuePath))
        {
            var process = new ProcessResult
            {
                ExitCode = -1,
                StandardError = "CUE source path is missing."
            };

            return new CueToolsRunResult
            {
                Process = process,
                CommandLine = string.Empty,
                StartedAt = startedAt
            };
        }

        var absoluteCuePath = Path.GetFullPath(cuePath);
        var toolDirectory = Path.GetDirectoryName(Path.GetFullPath(cueToolsPath));
        var workingDirectory = string.IsNullOrEmpty(toolDirectory) || !Directory.Exists(toolDirectory)
            ? Path.GetDirectoryName(absoluteCuePath) ?? string.Empty
            : toolDirectory;

        var arguments = CueToolsArgumentsBuilder.BuildVerifyArguments(absoluteCuePath);
        var commandLine = $"{cueToolsPath} {string.Join(" ", arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}";

        Log.Debug(
            "Running CUETools: {CommandLine} in {WorkingDirectory} (audio directory: {AudioDirectory})",
            commandLine,
            workingDirectory,
            audioDirectory);

        var processResult = await _processRunner.RunAsync(
            cueToolsPath,
            arguments,
            workingDirectory,
            cancellationToken,
            line => Log.Debug("CUETools stdout: {Line}", line),
            line => Log.Debug("CUETools stderr: {Line}", line));

        return new CueToolsRunResult
        {
            Process = processResult,
            CommandLine = commandLine,
            StartedAt = startedAt
        };
    }
}

