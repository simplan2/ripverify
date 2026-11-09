using System.Text.RegularExpressions;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;

namespace RipVerify.Infrastructure.Audio;

public sealed class OriginSignalReader : IOriginSignalReader
{
    private static readonly Regex ValidIsrcRegex = new(
        "^[A-Z]{2}[A-Z0-9]{3}[0-9]{7}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<OriginSignals> ReadAsync(AlbumMetadata album, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evidence = new List<string>();
        var tracks = album.Tracks ?? Array.Empty<TrackMetadata>();
        var validIsrcTracks = new List<(TrackMetadata Track, string Isrc)>();
        var seenInvalidIsrcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in tracks)
        {
            if (string.IsNullOrWhiteSpace(track.Isrc))
            {
                continue;
            }

            var original = track.Isrc.Trim();
            var normalized = original.ToUpperInvariant();
            if (!ValidIsrcRegex.IsMatch(normalized))
            {
                if (seenInvalidIsrcs.Add(original))
                {
                    evidence.Add($"Invalid ISRC format ignored: [{original}]");
                }

                continue;
            }

            validIsrcTracks.Add((track, normalized));
        }

        var hasPlatformIsrc = validIsrcTracks.Count > 0;
        string? platformIsrcExample = null;
        string? digitalDistributor = null;
        var hasStreamingOnlyTags = false;
        var streamingConfidence = 0.0;

        if (hasPlatformIsrc)
        {
            platformIsrcExample = validIsrcTracks[0].Isrc;
            var trackWord = validIsrcTracks.Count == 1 ? "track" : "tracks";
            evidence.Add($"ISRC detected (e.g., {platformIsrcExample}) in {validIsrcTracks.Count} {trackWord}");

            if (validIsrcTracks.Any(item => item.Isrc.StartsWith("QZ", StringComparison.Ordinal)))
            {
                evidence.Add("ISRC prefix QZ indicates Qobuz distribution");
                digitalDistributor = "QOBUZ";
            }
        }

        // Check distributor hints
        if (!string.IsNullOrEmpty(digitalDistributor))
        {
            hasStreamingOnlyTags = true;
            evidence.Add($"Digital distributor detected: {digitalDistributor}");
        }

        // Calculate StreamingConfidence
        // Formula: base 0.0, +0.3 for platform ISRC, +0.3 for streaming-only tags, +0.2 for known distributor
        if (hasPlatformIsrc)
        {
            streamingConfidence += 0.3;
        }
        if (hasStreamingOnlyTags)
        {
            streamingConfidence += 0.3;
        }
        if (!string.IsNullOrEmpty(digitalDistributor))
        {
            streamingConfidence += 0.2;
            if (new[] { "QOBUZ", "TIDAL", "APPLE", "SPOTIFY", "BELIEVE", "CDBABY", "DISTROKID" }.Contains(digitalDistributor))
            {
                streamingConfidence += 0.1;
            }
        }

        // A release where most tracks carry an ISRC is a strong platform-download signal,
        // even when the ISRC prefix is not Qobuz-specific.
        if (tracks.Count > 0 && validIsrcTracks.Count * 2 > tracks.Count)
        {
            streamingConfidence = Math.Max(streamingConfidence, 0.7);
        }

        streamingConfidence = Math.Min(1.0, streamingConfidence);

        if (evidence.Count == 0)
        {
            evidence.Add("No streaming origin signals detected");
        }

        return Task.FromResult(new OriginSignals
        {
            HasPlatformIsrc = hasPlatformIsrc,
            PlatformIsrcExample = platformIsrcExample,
            DigitalDistributor = digitalDistributor,
            HasStreamingOnlyTags = hasStreamingOnlyTags,
            StreamingConfidence = streamingConfidence,
            Evidence = evidence
        });
    }
}
