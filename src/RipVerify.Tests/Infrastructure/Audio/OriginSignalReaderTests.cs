namespace RipVerify.Tests.Infrastructure.Audio;

using FluentAssertions;
using RipVerify.Core.Models;
using RipVerify.Infrastructure.Audio;
using Xunit;

public class OriginSignalReaderTests
{
    [Fact]
    public async Task PlatformIsrcInMostTracks_RaisesConfidenceAndGroupsEvidence()
    {
        var isrcs = new[]
        {
            "USAT29902070", "USAT20001315", "USAT29902071", "USAT20001316",
            "USAT29902069", "USAT20001317", "USAT29902074", "USAT20001318",
            "USAT29902068", "USAT20001319", "USAT20001321", "USAT20001320"
        };
        var album = new AlbumMetadata
        {
            Tracks = isrcs
                .Select((isrc, index) => new TrackMetadata
                {
                    Number = index + 1,
                    Title = $"Track {index + 1}",
                    Isrc = isrc
                })
                .ToList()
        };

        var signals = await new OriginSignalReader().ReadAsync(album, CancellationToken.None);

        signals.HasPlatformIsrc.Should().BeTrue();
        signals.PlatformIsrcExample.Should().Be("USAT29902070");
        signals.StreamingConfidence.Should().BeApproximately(0.7, 0.0001);
        signals.Evidence.Should().ContainSingle(item => item.Contains("ISRC detected"));
        signals.Evidence.Should().Contain("ISRC detected (e.g., USAT29902070) in 12 tracks");
    }

    [Fact]
    public async Task PlatformIsrcInHalfTracks_DoesNotApplyMajorityBoost()
    {
        var album = new AlbumMetadata
        {
            Tracks = new[]
            {
                new TrackMetadata { Number = 1, Isrc = "USAT29902070" },
                new TrackMetadata { Number = 2, Isrc = "USAT20001315" },
                new TrackMetadata { Number = 3 },
                new TrackMetadata { Number = 4 }
            }
        };

        var signals = await new OriginSignalReader().ReadAsync(album, CancellationToken.None);

        signals.HasPlatformIsrc.Should().BeTrue();
        signals.StreamingConfidence.Should().BeApproximately(0.3, 0.0001);
        signals.Evidence.Should().Contain("ISRC detected (e.g., USAT29902070) in 2 tracks");
    }

    [Fact]
    public async Task InvalidIsrc_IsIgnoredForStreamingConfidence_AndReportedInEvidence()
    {
        var album = new AlbumMetadata
        {
            Tracks = new[]
            {
                new TrackMetadata { Number = 1, Isrc = "INVALID" },
                new TrackMetadata { Number = 2, Isrc = "USAT2990207" },
                new TrackMetadata { Number = 3, Isrc = "USAT29902070" }
            }
        };

        var signals = await new OriginSignalReader().ReadAsync(album, CancellationToken.None);

        signals.HasPlatformIsrc.Should().BeTrue();
        signals.PlatformIsrcExample.Should().Be("USAT29902070");
        signals.StreamingConfidence.Should().BeApproximately(0.3, 0.0001);
        signals.Evidence.Should().Contain("Invalid ISRC format ignored: [INVALID]");
        signals.Evidence.Should().Contain("Invalid ISRC format ignored: [USAT2990207]");
        signals.Evidence.Should().Contain("ISRC detected (e.g., USAT29902070) in 1 track");
    }

    [Fact]
    public async Task OnlyInvalidIsrc_DoesNotRaiseConfidence()
    {
        var album = new AlbumMetadata
        {
            Tracks = new[]
            {
                new TrackMetadata { Number = 1, Isrc = "NOT-AN-ISRC" },
                new TrackMetadata { Number = 2, Isrc = "123456789012" }
            }
        };

        var signals = await new OriginSignalReader().ReadAsync(album, CancellationToken.None);

        signals.HasPlatformIsrc.Should().BeFalse();
        signals.StreamingConfidence.Should().Be(0);
        signals.Evidence.Should().Contain("Invalid ISRC format ignored: [NOT-AN-ISRC]");
    }
}
