using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;

namespace RipVerify.Infrastructure.Cue;

public sealed class CueGenerator : ICueGenerator
{
    private readonly IMusicBrainzClient _musicBrainzClient;
    private readonly ILogger<CueGenerator> _logger;
    private static readonly string[] TocWarning = new[] { "CueFixer_GeneratedFromToc" };
    private static readonly string[] DurationWarning = new[] { "CueFixer_GeneratedFromDurations" };
    private static readonly string[] SingleImageWarning = new[] { "CueFixer_SingleImageNoToc" };

    private static readonly Action<ILogger, Exception?> _logDurationsNotMonotonic = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(1, nameof(GenerateAsync)),
        "Track durations are missing or invalid");
    private static readonly Action<ILogger, Exception?> _logSingleImageNoToc = LoggerMessage.Define(
        LogLevel.Warning,
        new EventId(2, nameof(GenerateAsync)),
        "Single image without TOC, CUE generation not reliable");

    public CueGenerator(IMusicBrainzClient musicBrainzClient, ILogger<CueGenerator> logger)
    {
        _musicBrainzClient = musicBrainzClient;
        _logger = logger;
    }

    public async Task<CueSheet?> GenerateAsync(AlbumMetadata metadata, CancellationToken cancellationToken)
    {
        if (metadata.Tracks.Count == 0)
        {
            return null;
        }

        if (metadata.Tracks.Any(track => track.Duration <= TimeSpan.Zero))
        {
            _logDurationsNotMonotonic(_logger, null);
            return null;
        }

        if (string.IsNullOrEmpty(metadata.MusicBrainzReleaseId))
        {
            var toc = BuildToc(metadata);
            if (!string.IsNullOrEmpty(toc))
            {
                var mbMetadata = await _musicBrainzClient.FindReleaseByTocAsync(toc, cancellationToken);
                if (mbMetadata is not null && mbMetadata.Tracks.Count > 0)
                {
                    return GenerateFromMetadata(metadata, mbMetadata, true, TocWarning);
                }
            }

            if (metadata.Tracks.Count == 1)
            {
                _logSingleImageNoToc(_logger, null);
                return null;
            }

            return GenerateFromMetadata(metadata, metadata, false, DurationWarning);
        }

        var mbMeta = await _musicBrainzClient.FindReleaseByTocAsync(string.Empty, cancellationToken);
        if (mbMeta is not null && mbMeta.Tracks.Count > 0)
        {
            return GenerateFromMetadata(metadata, mbMeta, true, TocWarning);
        }

        return GenerateFromMetadata(metadata, metadata, false, DurationWarning);
    }

    private static string BuildToc(AlbumMetadata metadata)
    {
        return string.Empty;
    }

    private static CueSheet GenerateFromMetadata(AlbumMetadata source, AlbumMetadata tocMetadata, bool fromToc, IReadOnlyList<string> notes)
    {
        var tracks = new List<CueTrack>();
        for (var i = 0; i < source.Tracks.Count; i++)
        {
            var trackFile = Path.GetFileName(source.Tracks[i].FilePath);
            tracks.Add(new CueTrack
            {
                Number = source.Tracks[i].Number,
                FileName = trackFile,
                Start = TimeSpan.Zero,
                Pregap = TimeSpan.Zero
            });
        }

        return new CueSheet
        {
            FilePath = string.Empty,
            Encoding = "UTF-8",
            Tracks = tracks,
            WasGenerated = true,
            RepairNotes = notes,
            Origin = fromToc ? CueOrigin.GeneratedFromTocLookup : CueOrigin.GeneratedFromMetadata
        };
    }
}
