using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Infrastructure.Audio;

namespace RipVerify.Infrastructure.FileSystem;

public sealed class FolderScanner : IFolderScanner
{
    private static readonly string[] AudioExtensions = { ".flac", ".wav", ".ape", ".wv" };

    private readonly ICueMetadataReader _metadataReader;
    private readonly ICueSanitizer? _cueSanitizer;

    public FolderScanner(ICueMetadataReader metadataReader, ICueSanitizer? cueSanitizer = null)
    {
        _metadataReader = metadataReader;
        _cueSanitizer = cueSanitizer;
    }

    public async Task<ScanResult> ScanAsync(ScanInput input, CancellationToken cancellationToken)
    {
        if (input.Paths.Count == 0)
        {
            return new ScanResult { Kind = ScanInputKind.Unknown, Errors = new[] { "No paths provided" } };
        }

        var directories = input.Paths.Where(p => Directory.Exists(p)).ToList();
        var files = input.Paths.Where(p => File.Exists(p) && IsAudioFile(p)).ToList();

        if (directories.Count == 1 && files.Count == 0)
        {
            return await ScanDirectoryAsync(directories[0], cancellationToken);
        }

        if (directories.Count == 0 && files.Count > 0)
        {
            return await ScanLooseFilesAsync(files, cancellationToken);
        }

        if (directories.Count >= 1 && files.Count > 0)
        {
            var result = await ScanDirectoryAsync(directories[0], cancellationToken);
            var loose = await ScanLooseFilesAsync(files, cancellationToken);
            var warnings = result.Warnings.Concat(loose.Warnings).ToList();
            return result with { Warnings = warnings };
        }

        return new ScanResult { Kind = ScanInputKind.Unknown, Warnings = new[] { "Scanner_MultipleAlbumsInLooseFiles" } };
    }

    private async Task<ScanResult> ScanDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        var cueFiles = Directory.GetFiles(directory, "*.cue", SearchOption.TopDirectoryOnly);
        var audioFiles = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(IsAudioFile)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var imageFiles = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (cueFiles.Length == 0 && audioFiles.Count == 0 && imageFiles.Count == 0)
        {
            return new ScanResult { Kind = ScanInputKind.Unknown, AlbumFolder = directory, Warnings = new[] { "Scanner_NoAudioFound" } };
        }

        var metadata = audioFiles.Count > 0
            ? await _metadataReader.ReadAlbumMetadataAsync(audioFiles, cancellationToken)
            : null;
        if (metadata is not null)
        {
            metadata = ValidateTrackIntegrity(metadata);

            if (cueFiles.Length > 0 && _cueSanitizer is not null)
            {
                var cue = await _cueSanitizer.SanitizeAsync(cueFiles[0], cancellationToken);
                metadata = MergeCueMetadata(metadata, cue);
            }
        }

        if (cueFiles.Length > 0 && audioFiles.Count == 1)
        {
            return new ScanResult { Kind = ScanInputKind.SingleImageWithCue, AlbumFolder = directory, CuePath = cueFiles[0], AudioFilePaths = audioFiles, Album = metadata };
        }

        if (cueFiles.Length > 0 && audioFiles.Count > 1)
        {
            return new ScanResult { Kind = ScanInputKind.AlbumFolder, AlbumFolder = directory, CuePath = cueFiles[0], AudioFilePaths = audioFiles, Album = metadata };
        }

        if (cueFiles.Length == 0 && audioFiles.Count > 1)
        {
            return new ScanResult { Kind = ScanInputKind.AlbumFolder, AlbumFolder = directory, AudioFilePaths = audioFiles, Album = metadata };
        }

        if (cueFiles.Length == 0 && audioFiles.Count == 1)
        {
            return new ScanResult { Kind = ScanInputKind.SingleImageWithoutCue, AlbumFolder = directory, AudioFilePaths = audioFiles, Album = metadata, Warnings = new[] { "Scanner_SingleTrackLooseFile" } };
        }

        if (cueFiles.Length == 0 && imageFiles.Count == 1 && audioFiles.Count == 0)
        {
            return new ScanResult { Kind = ScanInputKind.SingleImageWithoutCue, AlbumFolder = directory };
        }

        return new ScanResult { Kind = ScanInputKind.Unknown, AlbumFolder = directory };
    }

    private static bool IsAudioFile(string path)
        => AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static AlbumMetadata ValidateTrackIntegrity(AlbumMetadata album)
    {
        var tracks = album.Tracks;
        var positiveDurationTracks = tracks
            .Select((track, index) => (track, index))
            .Where(item => item.track.Duration > TimeSpan.Zero)
            .ToList();

        if (positiveDurationTracks.Count == 0)
        {
            return album;
        }

        var updatedTracks = new List<TrackMetadata>(tracks.Count);
        var changed = false;
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            var otherDurations = tracks
                .Where((_, otherIndex) => otherIndex != index)
                .Select(other => other.Duration)
                .Where(duration => duration > TimeSpan.Zero)
                .ToList();

            var isIncomplete = track.IsIncomplete;
            if (!isIncomplete && otherDurations.Count > 0)
            {
                if (track.Duration <= TimeSpan.Zero)
                {
                    isIncomplete = true;
                }
                else
                {
                    var averageSeconds = otherDurations.Average(duration => duration.TotalSeconds);
                    isIncomplete = track.Duration.TotalSeconds < averageSeconds * 0.5;
                }
            }

            if (!isIncomplete)
            {
                updatedTracks.Add(track);
                continue;
            }

            var warnings = track.Warnings.Contains("TrackIncomplete", StringComparer.Ordinal)
                ? track.Warnings
                : track.Warnings.Append("TrackIncomplete").ToArray();
            var updated = track with { IsIncomplete = true, Warnings = warnings };
            if (!ReferenceEquals(updated, track))
            {
                changed = true;
            }

            updatedTracks.Add(updated);
        }

        return changed ? album with { Tracks = updatedTracks } : album;
    }

    private static AlbumMetadata MergeCueMetadata(AlbumMetadata album, CueSheet cue)
    {
        var albumTitle = string.IsNullOrWhiteSpace(cue.AlbumTitle)
            ? album.Album
            : cue.AlbumTitle.Trim();
        var artist = string.IsNullOrWhiteSpace(cue.Artist)
            ? album.Artist
            : cue.Artist.Trim();

        var tracks = album.Tracks;
        if (cue.Tracks.Count > 0 && cue.Tracks.Count == album.Tracks.Count)
        {
            var cueByNumber = cue.Tracks
                .GroupBy(track => track.Number)
                .ToDictionary(group => group.Key, group => group.First());

            var updatedTracks = new List<TrackMetadata>(album.Tracks.Count);
            for (var index = 0; index < album.Tracks.Count; index++)
            {
                var track = album.Tracks[index];
                var cueTrack = cueByNumber.TryGetValue(track.Number, out var byNumber)
                    ? byNumber
                    : cue.Tracks[index];
                var title = string.IsNullOrWhiteSpace(cueTrack.Title)
                    ? track.Title
                    : cueTrack.Title.Trim();
                updatedTracks.Add(string.Equals(title, track.Title, StringComparison.Ordinal)
                    ? track
                    : track with { Title = title });
            }

            tracks = updatedTracks;
        }

        var cueProvidedAlbum = !string.IsNullOrWhiteSpace(cue.AlbumTitle);
        var cueProvidedAllTitles = cue.Tracks.Count == album.Tracks.Count &&
                                   cue.Tracks.All(track => !string.IsNullOrWhiteSpace(track.Title));
        var metadataFallbackUsed = album.MetadataFallbackUsed &&
                                   !(cueProvidedAlbum && cueProvidedAllTitles);

        return album with
        {
            Album = albumTitle,
            Artist = artist,
            Tracks = tracks,
            MetadataFallbackUsed = metadataFallbackUsed
        };
    }

    private async Task<ScanResult> ScanLooseFilesAsync(List<string> files, CancellationToken cancellationToken)
    {
        var metadata = ValidateTrackIntegrity(
            await _metadataReader.ReadAlbumMetadataAsync(files, cancellationToken));
        var groups = files
            .Select(f =>
            {
                try
                {
                    using var file = TagLib.File.Create(f);
                    var album = MetadataFallback.ResolveAlbum(file.Tag.Album, f);
                    var artist = (file.Tag.FirstAlbumArtist ?? file.Tag.FirstPerformer ?? string.Empty).Trim();
                    return (file: f, album, artist);
                }
                catch
                {
                    return (file: f, album: MetadataFallback.GetFolderName(f), artist: string.Empty);
                }
            })
            .GroupBy(g => (album: g.album.ToUpperInvariant(), artist: g.artist.ToUpperInvariant()))
            .ToList();

        if (groups.Count == 1 && files.Count >= 2)
        {
            var first = groups[0].First();
            var albumMeta = new AlbumMetadata
            {
                Album = string.IsNullOrWhiteSpace(first.album) ? metadata.Album : first.album,
                Artist = string.IsNullOrWhiteSpace(first.artist) ? metadata.Artist : first.artist,
                Tracks = metadata.Tracks,
                MetadataFallbackUsed = metadata.MetadataFallbackUsed,
                DbPoweramp = metadata.DbPoweramp
            };
            return new ScanResult { Kind = ScanInputKind.LooseFiles, LooseFilePaths = files, Album = albumMeta };
        }

        if (groups.Count == 1 && files.Count == 1)
        {
            var first = groups[0].First();
            var albumMeta = new AlbumMetadata
            {
                Album = string.IsNullOrWhiteSpace(first.album) ? metadata.Album : first.album,
                Artist = string.IsNullOrWhiteSpace(first.artist) ? metadata.Artist : first.artist,
                Tracks = metadata.Tracks,
                MetadataFallbackUsed = metadata.MetadataFallbackUsed,
                DbPoweramp = metadata.DbPoweramp
            };
            return new ScanResult { Kind = ScanInputKind.LooseFiles, LooseFilePaths = files, Album = albumMeta, Warnings = new[] { "Scanner_SingleTrackLooseFile" } };
        }

        return new ScanResult { Kind = ScanInputKind.LooseFiles, LooseFilePaths = files, Album = null, Warnings = new[] { "Scanner_MultipleAlbumsInLooseFiles" } };
    }
}
