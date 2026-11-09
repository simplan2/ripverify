using System.Text;
using Microsoft.Extensions.Logging;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Infrastructure.FileSystem;

namespace RipVerify.Infrastructure.Cue;

public sealed class CueSanitizer : ICueSanitizer
{
    private readonly ILogger<CueSanitizer> _logger;
    private static readonly string[] LineSplitArray = new[] { "\r\n", "\n", "\r" };
    private static readonly char[] SpaceSplitChars = new[] { ' ', '\t' };

    private static readonly Action<ILogger, string, Exception?> _logNoTracks = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(1, nameof(SanitizeAsync)),
        "CUE file has no tracks: {CuePath}");
    private static readonly Action<ILogger, string, Exception?> _logFailed = LoggerMessage.Define<string>(
        LogLevel.Error,
        new EventId(2, nameof(SanitizeAsync)),
        "Failed to sanitize CUE file: {CuePath}");

    public CueSanitizer(ILogger<CueSanitizer> logger)
    {
        _logger = logger;
    }

    public async Task<CueSheet> SanitizeAsync(string cuePath, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(cuePath, cancellationToken);
            string content;
            bool hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

            if (hadBom)
            {
                content = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }
            else
            {
                try
                {
                    content = Encoding.UTF8.GetString(bytes);
                    if (content.Contains('\uFFFD', StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Invalid UTF-8");
                    }
                }
                catch
                {
                    content = Encoding.GetEncoding(1252).GetString(bytes);
                }
            }

            var repairNotes = new List<string>();
            var cueDir = Path.GetDirectoryName(cuePath) ?? string.Empty;
            var lines = content.Split(LineSplitArray, StringSplitOptions.None);
            var modified = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
                {
                    var start = line.IndexOf('"');
                    var end = line.LastIndexOf('"');
                    if (start >= 0 && end > start)
                    {
                        var fileName = line.Substring(start + 1, end - start - 1);
                        if (PathNormalizer.IsAbsolute(fileName))
                        {
                            var relative = PathNormalizer.MakeRelative(cueDir, fileName);
                            lines[i] = string.Concat(line.AsSpan(0, start + 1), relative, line.AsSpan(end));
                            repairNotes.Add("Absolute path converted to relative");
                            modified = true;
                        }
                        else if (!File.Exists(Path.Combine(cueDir, fileName)))
                        {
                            var files = Directory.GetFiles(cueDir);
                            var match = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
                            if (match is not null)
                            {
                                lines[i] = string.Concat(line.AsSpan(0, start + 1), Path.GetFileName(match), line.AsSpan(end));
                                repairNotes.Add("Filename matched case-insensitively");
                                modified = true;
                            }
                        }
                    }
                }
            }

            var tracks = ParseTracks(lines, out var albumTitle, out var artist);
            if (tracks.Count == 0)
            {
                _logNoTracks(_logger, cuePath, null);
            }

            var outputPath = cuePath;
            if (modified)
            {
                outputPath = CueFileNaming.WithSuffix(cuePath, CueFileNaming.RepairedSuffix);
                var newContent = CueProvenance.BuildHeader(CueOrigin.Sanitized) + string.Join("\r\n", lines);
                var utf8 = new UTF8Encoding(false);
                await File.WriteAllTextAsync(outputPath, newContent, utf8, cancellationToken);
            }

            return new CueSheet
            {
                FilePath = outputPath,
                Encoding = "UTF-8",
                AlbumTitle = albumTitle,
                Artist = artist,
                Tracks = tracks,
                WasGenerated = false,
                RepairNotes = repairNotes,
                Origin = CueProvenance.ResolveOrigin(cuePath, content, modified)
            };
        }
        catch (Exception ex)
        {
            _logFailed(_logger, cuePath, ex);
            return new CueSheet
            {
                FilePath = cuePath,
                Encoding = "UTF-8",
                Tracks = Array.Empty<CueTrack>(),
                WasGenerated = false,
                RepairNotes = Array.Empty<string>(),
                Origin = CueProvenance.ResolveOrigin(cuePath, string.Empty, modified: false)
            };
        }
    }

    private static List<CueTrack> ParseTracks(string[] lines, out string albumTitle, out string artist)
    {
        var tracks = new List<CueTrack>();
        int currentTrack = 0;
        string currentFile = string.Empty;
        string currentTitle = string.Empty;
        TimeSpan currentStart = TimeSpan.Zero;
        TimeSpan? currentPregap = null;
        albumTitle = string.Empty;
        artist = string.Empty;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
            {
                currentFile = ExtractQuotedValue(trimmed);
            }
            else if (trimmed.StartsWith("TRACK", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split(SpaceSplitChars, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var trackNum))
                {
                    currentTrack = trackNum;
                    currentTitle = string.Empty;
                }
            }
            else if (trimmed.StartsWith("TITLE", StringComparison.OrdinalIgnoreCase))
            {
                var title = ExtractQuotedValue(trimmed);
                if (currentTrack > 0)
                {
                    currentTitle = title;
                }
                else if (string.IsNullOrWhiteSpace(albumTitle))
                {
                    albumTitle = title;
                }
            }
            else if (trimmed.StartsWith("PERFORMER", StringComparison.OrdinalIgnoreCase))
            {
                var performer = ExtractQuotedValue(trimmed);
                if (currentTrack == 0 && string.IsNullOrWhiteSpace(artist))
                {
                    artist = performer;
                }
            }
            else if (trimmed.StartsWith("PREGAP", StringComparison.OrdinalIgnoreCase))
            {
                currentPregap = ParseTime(trimmed);
            }
            else if (trimmed.StartsWith("INDEX 01", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("INDEX 1", StringComparison.OrdinalIgnoreCase))
            {
                currentStart = ParseTime(trimmed);
                tracks.Add(new CueTrack
                {
                    Number = currentTrack,
                    FileName = currentFile,
                    Title = currentTitle,
                    Start = currentStart,
                    Pregap = currentPregap
                });
                currentPregap = null;
                currentTrack = 0;
                currentTitle = string.Empty;
            }
        }

        return tracks;
    }

    private static string ExtractQuotedValue(string line)
    {
        var start = line.IndexOf('"');
        var end = line.LastIndexOf('"');
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        return line.Substring(start + 1, end - start - 1).Replace("\\\"", "\"", StringComparison.Ordinal);
    }

    private static TimeSpan ParseTime(string line)
    {
        var parts = line.Split(SpaceSplitChars, StringSplitOptions.RemoveEmptyEntries);
        var timePart = parts[^1];
        var timeParts = timePart.Split(':');
        if (timeParts.Length == 3 && int.TryParse(timeParts[0], out var min) && int.TryParse(timeParts[1], out var sec) && int.TryParse(timeParts[2], out var frames))
        {
            return new TimeSpan(0, 0, min, sec, frames * 1000 / 75);
        }
        return TimeSpan.Zero;
    }
}
