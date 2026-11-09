using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;

namespace RipVerify.Infrastructure.Audio;

public sealed class TagLibMetadataReader : ICueMetadataReader
{
    private static readonly string[] TrackIncompleteWarnings = { "TrackIncomplete" };

    public Task<AlbumMetadata> ReadAlbumMetadataAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken)
    {
        var tracks = new List<TrackMetadata>();
        var dbPowerampTracks = new List<DbPowerampTrackEvidence>();
        string? taggedAlbum = null;
        string artist = string.Empty;
        string? accurateRipDiscId = null;
        string? accurateRipResult = null;
        var hasAccurate = false;
        var hasInaccurate = false;
        var metadataFallbackUsed = false;

        for (var i = 0; i < filePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = filePaths[i];
            try
            {
                using var file = TagLib.File.Create(filePath);
                var discId = ReadTagValue(file, "ACCURATERIPDISCID");
                var ripResult = ReadTagValue(file, "ACCURATERIPRESULT");
                if (!string.IsNullOrWhiteSpace(discId) || !string.IsNullOrWhiteSpace(ripResult))
                {
                    accurateRipDiscId ??= discId;
                    accurateRipResult ??= ripResult;
                    var isAccurate = IsAccurateResult(ripResult);
                    if (isAccurate)
                    {
                        hasAccurate = true;
                    }
                    else if (IsInaccurateResult(ripResult))
                    {
                        hasInaccurate = true;
                    }

                    dbPowerampTracks.Add(new DbPowerampTrackEvidence
                    {
                        TrackNumber = i + 1,
                        AccurateRipResult = ripResult ?? string.Empty,
                        IsAccurate = isAccurate
                    });
                }

                var candidateAlbum = file.Tag.Album;
                if (!string.IsNullOrWhiteSpace(candidateAlbum) &&
                    (string.IsNullOrWhiteSpace(taggedAlbum) || MetadataFallback.IsGenericAlbum(taggedAlbum)))
                {
                    taggedAlbum = candidateAlbum;
                }
                if (MetadataFallback.IsGenericAlbum(candidateAlbum))
                {
                    metadataFallbackUsed = true;
                }
                if (string.IsNullOrWhiteSpace(artist))
                {
                    artist = (file.Tag.FirstAlbumArtist ?? file.Tag.FirstPerformer ?? string.Empty).Trim();
                }

                string? isrc = null;
                if (file.TagTypes.HasFlag(TagLib.TagTypes.Xiph) && file.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment xiphTag)
                {
                    var isrcValues = xiphTag.GetField("ISRC");
                    if (isrcValues != null && isrcValues.Length > 0)
                    {
                        isrc = isrcValues[0];
                    }
                }

                var taggedTitle = file.Tag.Title;
                if (string.IsNullOrWhiteSpace(taggedTitle) || MetadataFallback.IsGenericTrackTitle(taggedTitle))
                {
                    metadataFallbackUsed = true;
                }

                tracks.Add(new TrackMetadata
                {
                    Number = i + 1,
                    Title = MetadataFallback.ResolveTrackTitle(taggedTitle, filePath),
                    Isrc = isrc,
                    Duration = file.Properties.Duration,
                    FilePath = filePath
                });
            }
            catch
            {
                metadataFallbackUsed = true;
                tracks.Add(new TrackMetadata
                {
                    Number = i + 1,
                    Title = MetadataFallback.ResolveTrackTitle(null, filePath),
                    Isrc = null,
                    Duration = TimeSpan.Zero,
                    FilePath = filePath,
                    IsIncomplete = true,
                    Warnings = TrackIncompleteWarnings
                });
            }
        }

        var album = filePaths.Count > 0
            ? MetadataFallback.ResolveAlbum(taggedAlbum, filePaths[0])
            : string.Empty;
        if (string.IsNullOrWhiteSpace(taggedAlbum))
        {
            metadataFallbackUsed = true;
        }

        var dbPoweramp = dbPowerampTracks.Count == 0 &&
                         string.IsNullOrWhiteSpace(accurateRipDiscId) &&
                         string.IsNullOrWhiteSpace(accurateRipResult)
            ? DbPowerampEvidence.Empty
            : new DbPowerampEvidence
            {
                AccurateRipDiscId = accurateRipDiscId,
                AccurateRipResult = accurateRipResult,
                IsAccurate = hasAccurate && !hasInaccurate,
                Tracks = dbPowerampTracks
            };

        return Task.FromResult(new AlbumMetadata
        {
            Album = album,
            Artist = artist,
            MusicBrainzReleaseId = null,
            Tracks = tracks,
            MetadataFallbackUsed = metadataFallbackUsed,
            DbPoweramp = dbPoweramp
        });
    }

    private static string? ReadTagValue(TagLib.File file, string key)
    {
        if (file.TagTypes.HasFlag(TagLib.TagTypes.Xiph) &&
            file.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiphTag)
        {
            var values = xiphTag.GetField(key);
            if (values is { Length: > 0 } && !string.IsNullOrWhiteSpace(values[0]))
            {
                return values[0].Trim();
            }
        }

        if (file.TagTypes.HasFlag(TagLib.TagTypes.Id3v2) &&
            file.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3Tag)
        {
            var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3Tag, key, false);
            if (frame is not null && frame.Text.Length > 0 && !string.IsNullOrWhiteSpace(frame.Text[0]))
            {
                return frame.Text[0].Trim();
            }
        }

        if (file.TagTypes.HasFlag(TagLib.TagTypes.Ape) &&
            file.GetTag(TagLib.TagTypes.Ape, false) is TagLib.Ape.Tag apeTag)
        {
            var item = apeTag.GetItem(key);
            if (item is not null)
            {
                var value = item.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static bool IsAccurateResult(string? result)
    {
        return !string.IsNullOrWhiteSpace(result) &&
               result.TrimStart().StartsWith("Accurate", StringComparison.OrdinalIgnoreCase) &&
               !result.Contains("Inaccurate", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInaccurateResult(string? result)
    {
        return !string.IsNullOrWhiteSpace(result) &&
               result.Contains("Inaccurate", StringComparison.OrdinalIgnoreCase);
    }
}
