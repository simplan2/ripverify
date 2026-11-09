using System.Globalization;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Infrastructure.Audio;

namespace RipVerify.Infrastructure.Logs;

public sealed class RipLogVerifier : IRipLogVerifier
{
    public const string VirtualDriveWarning =
        "Rip verificado por log, pero origen físico no confirmado (unidad virtual detectada)";

    private readonly AudioCrc32Reader _crcReader;

    public RipLogVerifier(AudioCrc32Reader crcReader)
    {
        _crcReader = crcReader;
    }

    public async Task<RipLogVerification> VerifyAsync(
        AlbumMetadata album,
        string? albumFolder,
        IReadOnlyList<string> audioFilePaths,
        CancellationToken cancellationToken)
    {
        var folder = ResolveFolder(album, albumFolder, audioFilePaths);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return RipLogVerification.Empty;
        }

        var log = await FindLogAsync(folder, cancellationToken).ConfigureAwait(false);
        if (log is null)
        {
            return RipLogVerification.Empty;
        }

        var pathsByNumber = BuildTrackPathMap(album, audioFilePaths);
        var comparedTracks = 0;
        var matchedTracks = 0;
        var mismatchedTracks = new List<int>();
        var warnings = new List<string>();
        var evidence = new List<string>();
        var crcCache = new Dictionary<string, uint?>(StringComparer.OrdinalIgnoreCase);

        if (log.IsVirtualDrive)
        {
            warnings.Add(VirtualDriveWarning);
        }

        foreach (var logTrack in log.Tracks.OrderBy(track => track.Number))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!pathsByNumber.TryGetValue(logTrack.Number, out var audioPath) ||
                string.IsNullOrWhiteSpace(audioPath) ||
                !File.Exists(audioPath))
            {
                warnings.Add($"No se encontró el archivo de audio para la pista {logTrack.Number}");
                continue;
            }

            var expectedCrc = logTrack.CopyCrc ?? logTrack.TestCrc;
            if (string.IsNullOrWhiteSpace(expectedCrc))
            {
                continue;
            }

            if (!crcCache.TryGetValue(audioPath, out var actualCrc))
            {
                actualCrc = await _crcReader.ComputeAsync(audioPath, cancellationToken).ConfigureAwait(false);
                crcCache[audioPath] = actualCrc;
            }

            if (!actualCrc.HasValue)
            {
                warnings.Add($"No se pudo calcular el CRC de la pista {logTrack.Number}");
                continue;
            }

            comparedTracks++;
            var actualText = actualCrc.Value.ToString("X8", CultureInfo.InvariantCulture);
            if (string.Equals(actualText, expectedCrc, StringComparison.OrdinalIgnoreCase))
            {
                matchedTracks++;
                evidence.Add($"Pista {logTrack.Number}: CRC {actualText} coincide con el log");
            }
            else
            {
                mismatchedTracks.Add(logTrack.Number);
                evidence.Add($"Pista {logTrack.Number}: CRC {actualText} no coincide con {expectedCrc}");
            }
        }

        if (pathsByNumber.Count > 0 && log.Tracks.Count != pathsByNumber.Count)
        {
            warnings.Add(
                $"El log contiene {log.Tracks.Count} pistas y el álbum {pathsByNumber.Count}");
        }

        if (mismatchedTracks.Count > 0)
        {
            warnings.Add($"El CRC del log no coincide en las pistas: {string.Join(", ", mismatchedTracks)}");
        }

        if (comparedTracks == 0)
        {
            warnings.Add("No se pudo comparar ningún CRC del log");
        }

        return new RipLogVerification
        {
            LogPath = log.FilePath,
            DriveName = log.DriveName,
            IsVirtualDrive = log.IsVirtualDrive,
            LogTrackCount = log.Tracks.Count,
            ExpectedTrackCount = pathsByNumber.Count,
            ComparedTracks = comparedTracks,
            MatchedTracks = matchedTracks,
            MismatchedTrackNumbers = mismatchedTracks,
            Warnings = warnings,
            Evidence = evidence
        };
    }

    private static async Task<RipLogDocument?> FindLogAsync(string folder, CancellationToken cancellationToken)
    {
        RipLogDocument? best = null;
        foreach (var path in Directory.EnumerateFiles(folder, "*.log", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = await LogFileParser.ParseFileAsync(path, cancellationToken).ConfigureAwait(false);
            if (document is not null &&
                document.Tracks.Count > 0 &&
                (best is null || document.Tracks.Count > best.Tracks.Count))
            {
                best = document;
            }
        }

        return best;
    }

    private static Dictionary<int, string> BuildTrackPathMap(
        AlbumMetadata album,
        IReadOnlyList<string> audioFilePaths)
    {
        var result = new Dictionary<int, string>();
        var albumTracks = album.Tracks ?? Array.Empty<TrackMetadata>();

        foreach (var track in albumTracks)
        {
            if (!string.IsNullOrWhiteSpace(track.FilePath) && File.Exists(track.FilePath))
            {
                result[track.Number] = track.FilePath;
            }
        }

        var fallbackPaths = audioFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (fallbackPaths.Count == 0)
        {
            fallbackPaths = albumTracks
                .Where(track => !string.IsNullOrWhiteSpace(track.FilePath) && File.Exists(track.FilePath))
                .OrderBy(track => track.Number)
                .Select(track => track.FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        for (var index = 0; index < fallbackPaths.Count; index++)
        {
            var number = index + 1;
            if (!result.ContainsKey(number))
            {
                result[number] = fallbackPaths[index];
            }
        }

        return result;
    }

    private static string? ResolveFolder(
        AlbumMetadata album,
        string? albumFolder,
        IReadOnlyList<string> audioFilePaths)
    {
        if (!string.IsNullOrWhiteSpace(albumFolder) && Directory.Exists(albumFolder))
        {
            return albumFolder;
        }

        var firstTrackPath = album.Tracks
            .Select(track => track.FilePath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        if (!string.IsNullOrWhiteSpace(firstTrackPath))
        {
            return Path.GetDirectoryName(firstTrackPath);
        }

        var firstAudioPath = audioFilePaths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        return string.IsNullOrWhiteSpace(firstAudioPath) ? null : Path.GetDirectoryName(firstAudioPath);
    }
}
