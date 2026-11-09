using Microsoft.Extensions.Logging;

namespace RipVerify.Core.Services;

public sealed class FileValidator : IFileValidator
{
    private static readonly Action<ILogger, string, string, Exception?> _logFfmpegFound =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, nameof(FindFfmpeg)),
            "FFmpeg found at {Source}: {Path}");

    private static readonly Action<ILogger, Exception?> _logFfmpegNotFound =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(2, nameof(FindFfmpeg)),
            "FFmpeg not found; spectrum analysis will be unavailable.");

    private readonly ILogger<FileValidator> _logger;

    public FileValidator(ILogger<FileValidator> logger)
    {
        _logger = logger;
    }

    internal string BaseDirectory { get; set; } = AppContext.BaseDirectory;

    internal string ProgramFilesRoot { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    public bool IsCueToolsAvailable(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return true;
        }

        var found = FindInPath("CUETools.ARCUE.exe") ?? FindInPath("CUETools.Console.exe");
        return !string.IsNullOrEmpty(found);
    }

    public bool IsFfmpegAvailable(string? path) => FindFfmpeg(path) is not null;

    public string? FindFfmpeg(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            _logFfmpegFound(_logger, "configured path", configuredPath, null);
            return configuredPath;
        }

        var bundled = Path.Combine(BaseDirectory, "ffmpeg", "ffmpeg.exe");
        if (File.Exists(bundled))
        {
            _logFfmpegFound(_logger, "application directory", bundled, null);
            return bundled;
        }

        if (!string.IsNullOrWhiteSpace(ProgramFilesRoot))
        {
            var installed = Path.Combine(ProgramFilesRoot, "RipVerify", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(installed))
            {
                _logFfmpegFound(_logger, "installation directory", installed, null);
                return installed;
            }
        }

        var inPath = FindInPath("ffmpeg.exe");
        if (!string.IsNullOrEmpty(inPath))
        {
            _logFfmpegFound(_logger, "PATH", inPath, null);
            return inPath;
        }

        _logFfmpegNotFound(_logger, null);
        return null;
    }

    public string? FindInPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return null;
        }

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore invalid paths
            }
        }

        return null;
    }
}

