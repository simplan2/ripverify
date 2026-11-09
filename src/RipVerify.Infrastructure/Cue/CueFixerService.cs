using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;

namespace RipVerify.Infrastructure.Cue;

public sealed class CueFixerService : ICueFixerService
{
    private readonly IFolderScanner _folderScanner;
    private readonly ICueSanitizer _cueSanitizer;
    private readonly ICueGenerator _cueGenerator;
    private readonly ICueMetadataReader _metadataReader;
    private readonly ILogger<CueFixerService> _logger;
    private static readonly string[] SingleImageNoToc = new[] { "CueFixer_SingleImageNoToc" };
    private static readonly string[] NoCueAndNoAlbum = new[] { "CueFixer_NoCueAndNoAlbum" };
    private static readonly string[] GenerationFailed =
        new[] { "CUE generation failed: track durations are missing or invalid." };
    private static readonly string[] NoCueAndNoAlbumError =
        new[] { "No CUE sheet and no album metadata available for this folder." };
    private const string UnusualCueStructureWarning =
        "Estructura de CUE inusual. El audio puede ser válido, pero el archivo podría ser problemático en otros reproductores.";

    public CueFixerService(
        IFolderScanner folderScanner,
        ICueSanitizer cueSanitizer,
        ICueGenerator cueGenerator,
        ICueMetadataReader metadataReader,
        ILogger<CueFixerService> logger)
    {
        _folderScanner = folderScanner;
        _cueSanitizer = cueSanitizer;
        _cueGenerator = cueGenerator;
        _metadataReader = metadataReader;
        _logger = logger;
    }

    public async Task<CueFixResult> FixAsync(ScanResult scan, CancellationToken cancellationToken)
    {
        if (scan.Errors.Count > 0)
        {
            return new CueFixResult { Errors = scan.Errors };
        }

        if (scan.Kind == ScanInputKind.Unknown)
        {
            return new CueFixResult { Errors = scan.Errors, Warnings = scan.Warnings };
        }

        if (!string.IsNullOrEmpty(scan.CuePath) && File.Exists(scan.CuePath))
        {
            var sanitized = await _cueSanitizer.SanitizeAsync(scan.CuePath, cancellationToken);
            var warnings = scan.Warnings.ToList();
            if (HasUnusualStructure(sanitized, scan))
            {
                warnings.Add(UnusualCueStructureWarning);
            }

            return new CueFixResult
            {
                Cue = sanitized,
                WasRepaired = sanitized.RepairNotes.Count > 0,
                WasGenerated = false,
                Origin = sanitized.Origin,
                RepairNotes = sanitized.RepairNotes,
                Warnings = warnings
            };
        }

        if (scan.Album is not null && scan.Album.Tracks.Count > 0)
        {
            var generated = await _cueGenerator.GenerateAsync(scan.Album, cancellationToken);
            if (generated is null)
            {
                return new CueFixResult
                {
                    Warnings = SingleImageNoToc.Concat(scan.Warnings).ToList(),
                    Errors = GenerationFailed
                };
            }

            var albumFolder = scan.AlbumFolder ?? string.Empty;
            if (string.IsNullOrEmpty(albumFolder) && scan.LooseFilePaths.Count > 0)
            {
                albumFolder = Path.GetDirectoryName(scan.LooseFilePaths[0]) ?? string.Empty;
            }

            var cueName = string.IsNullOrWhiteSpace(scan.Album.Album) ? Path.GetFileName(albumFolder) : scan.Album.Album;
            var cueFilePath = Path.Combine(albumFolder, cueName + CueFileNaming.GeneratedSuffix + ".cue");
            var content = CueProvenance.BuildHeader(generated.Origin) + BuildCueContent(scan.Album, generated);
            var utf8 = new UTF8Encoding(false);
            await File.WriteAllTextAsync(cueFilePath, content, utf8, cancellationToken);
            generated = generated with { FilePath = cueFilePath };

            return new CueFixResult
            {
                Cue = generated,
                WasRepaired = false,
                WasGenerated = true,
                Origin = generated.Origin,
                RepairNotes = generated.RepairNotes,
                Warnings = scan.Warnings
            };
        }

        return new CueFixResult
        {
            Warnings = NoCueAndNoAlbum.Concat(scan.Warnings).ToList(),
            Errors = NoCueAndNoAlbumError
        };
    }

    private static bool HasUnusualStructure(CueSheet cue, ScanResult scan)
    {
        if (cue.Tracks.Count == 0)
        {
            return true;
        }

        var cueDirectory = Path.GetDirectoryName(cue.FilePath) ?? string.Empty;
        var trackNumbers = new HashSet<int>();
        foreach (var track in cue.Tracks)
        {
            if (!trackNumbers.Add(track.Number) ||
                string.IsNullOrWhiteSpace(track.FileName) ||
                track.Start < TimeSpan.Zero)
            {
                return true;
            }

            var candidate = Path.IsPathRooted(track.FileName)
                ? track.FileName
                : Path.Combine(cueDirectory, track.FileName);
            if (!File.Exists(candidate))
            {
                return true;
            }
        }

        if (scan.Kind != ScanInputKind.SingleImageWithCue &&
            scan.AudioFilePaths.Count > 0 &&
            scan.AudioFilePaths.Count != cue.Tracks.Count)
        {
            return true;
        }

        return false;
    }

    private static string BuildCueContent(AlbumMetadata album, CueSheet cue)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(album.Artist))
        {
            sb.AppendLine("PERFORMER \"" + album.Artist.Replace("\"", "\\\"") + "\"");
        }
        if (!string.IsNullOrEmpty(album.Album))
        {
            sb.AppendLine("TITLE \"" + album.Album.Replace("\"", "\\\"") + "\"");
        }

        string currentFile = string.Empty;
        foreach (var track in cue.Tracks)
        {
            if (track.FileName != currentFile)
            {
                currentFile = track.FileName;
                sb.AppendLine("FILE \"" + currentFile.Replace("\"", "\\\"") + "\" WAVE");
            }
            sb.AppendLine("  TRACK " + track.Number.ToString("00", CultureInfo.InvariantCulture) + " AUDIO");
            if (!string.IsNullOrEmpty(album.Artist))
            {
                sb.AppendLine("    PERFORMER \"" + album.Artist.Replace("\"", "\\\"") + "\"");
            }
            var trackMeta = album.Tracks.FirstOrDefault(t => t.Number == track.Number);
            if (trackMeta is not null && !string.IsNullOrEmpty(trackMeta.Title))
            {
                sb.AppendLine("    TITLE \"" + trackMeta.Title.Replace("\"", "\\\"") + "\"");
            }
            if (track.Pregap.HasValue && track.Pregap.Value > TimeSpan.Zero)
            {
                sb.AppendLine("    PREGAP " + FormatTime(track.Pregap.Value));
            }
            sb.AppendLine("    INDEX 01 " + FormatTime(track.Start));
        }

        return sb.ToString();
    }

    private static string FormatTime(TimeSpan time)
    {
        var totalSeconds = (int)time.TotalSeconds;
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        var frames = (int)((time.TotalSeconds - totalSeconds) * 75);
        return minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + seconds.ToString("00", CultureInfo.InvariantCulture) + ":" + frames.ToString("00", CultureInfo.InvariantCulture);
    }
}
