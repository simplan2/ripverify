namespace RipVerify.Tests.Infrastructure.Reporting;

using FluentAssertions;
using RipVerify.Core.Models;
using RipVerify.Infrastructure.Reporting;
using Xunit;

public class ReportDocumentBuilderTests
{
    [Fact]
    public void BuildMapsAlbumCueAndResult()
    {
        var builder = new ReportDocumentBuilder();
        var verifiedAt = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        var album = new AlbumMetadata { Album = "Test Album", Artist = "Test Artist" };
        var cue = new CueSheet { FilePath = @"C:\music\album.cue" };
        var result = new VerificationResult
        {
            AlbumPath = @"C:\music",
            CuePath = @"C:\music\album.cue",
            CueOrigin = CueOrigin.GeneratedFromMetadata,
            DbPoweramp = new DbPowerampEvidence
            {
                AccurateRipDiscId = "00142e60-00bd0bfe-950acb0c",
                AccurateRipResult = "Accurate (confidence 12)",
                IsAccurate = true
            },
            AccurateRipStatus = AccurateRipStatus.NotFound,
            ConfidenceScore = 11,
            CtdbStatus = CtdbStatus.AccuratelyRipped,
            CtdbConfidenceScore = 11,
            DriveOffset = 6,
            CtdbParityAvailable = true,
            Status = VerificationStatus.VerifiedWithWarnings,
            Diagnostics = new[] { "all checks passed" },
            ParserWarnings = new[] { "2 | (42/173) Differs in 58 samples" },
            Log = new RipLogVerification
            {
                LogPath = @"C:\music\album.log",
                LogTrackCount = 1,
                ComparedTracks = 1,
                MatchedTracks = 1
            },
            VerifiedAt = verifiedAt
        };

        var document = builder.Build(album, cue, result);

        document.SchemaVersion.Should().Be(ReportWriter.CurrentSchemaVersion);
        document.App.Should().Be("RipVerify");
        document.AppVersion.Should().NotBeNullOrWhiteSpace();
        document.VerifiedAt.Should().Be(verifiedAt);
        document.AlbumPath.Should().Be(@"C:\music");
        document.Album.Should().BeSameAs(album);
        document.Results.Should().ContainSingle();

        var entry = document.Results[0];
        entry.AlbumPath.Should().Be(@"C:\music");
        entry.CuePath.Should().Be(@"C:\music\album.cue");
        entry.CueOrigin.Should().Be(CueOrigin.GeneratedFromMetadata);
        entry.DbPoweramp.IsAccurate.Should().BeTrue();
        entry.DbPoweramp.AccurateRipDiscId.Should().Be("00142e60-00bd0bfe-950acb0c");
        entry.ParserWarnings.Should().ContainSingle();
        entry.AccurateRipStatus.Should().Be(AccurateRipStatus.NotFound);
        entry.ConfidenceScore.Should().Be(11);
        entry.CtdbStatus.Should().Be(CtdbStatus.AccuratelyRipped);
        entry.CtdbConfidenceScore.Should().Be(11);
        entry.DriveOffset.Should().Be(6);
        entry.CtdbParityAvailable.Should().BeTrue();
        entry.Status.Should().Be(VerificationStatus.VerifiedWithWarnings);
        entry.Diagnostics.Should().ContainSingle().Which.Should().Be("all checks passed");
        entry.Log.Should().NotBeNull();
        entry.Log!.LogPath.Should().Be(@"C:\music\album.log");
        entry.Log.MatchedTracks.Should().Be(1);
        entry.CueWarnings.Should().BeEmpty();
    }

    [Fact]
    public void BuildMapsCueWarnings()
    {
        var builder = new ReportDocumentBuilder();
        var cueWarnings = new[] { "Estructura de CUE inusual." };

        var document = builder.Build(
            new AlbumMetadata(),
            new CueSheet(),
            new VerificationResult(),
            cueWarnings);

        document.Results[0].CueWarnings.Should().Equal(cueWarnings);
    }

    [Fact]
    public void BuildFallsBackToCueDirectoryWhenAlbumPathMissing()
    {
        var builder = new ReportDocumentBuilder();
        var cue = new CueSheet { FilePath = @"C:\music\album.cue" };
        var result = new VerificationResult { AlbumPath = string.Empty };

        var document = builder.Build(new AlbumMetadata(), cue, result);

        document.AlbumPath.Should().Be(@"C:\music");
        document.Results[0].CuePath.Should().Be(@"C:\music\album.cue");
    }

    [Fact]
    public void BuildUsesCurrentTimeWhenResultTimestampIsDefault()
    {
        var builder = new ReportDocumentBuilder();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var document = builder.Build(new AlbumMetadata(), new CueSheet(), new VerificationResult());

        document.VerifiedAt.Should().BeAfter(before);
    }
}
