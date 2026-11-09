namespace RipVerify.Core.Services;

public interface IFileValidator
{
    bool IsCueToolsAvailable(string? path);
    bool IsFfmpegAvailable(string? path);
    string? FindFfmpeg(string? configuredPath);
    string? FindInPath(string fileName);
}
