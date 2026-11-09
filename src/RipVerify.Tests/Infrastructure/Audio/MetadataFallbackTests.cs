namespace RipVerify.Tests.Infrastructure.Audio;

using FluentAssertions;
using RipVerify.Infrastructure.Audio;
using Xunit;

public class MetadataFallbackTests
{
    [Fact]
    public void ResolveAlbumUsesFolderNameWhenTagIsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), "Artist - Album", "01 Track.flac");

        MetadataFallback.ResolveAlbum(null, path).Should().Be("Artist - Album");
        MetadataFallback.ResolveAlbum("   ", path).Should().Be("Artist - Album");
    }

    [Fact]
    public void ResolveAlbumKeepsRealTag()
    {
        var path = Path.Combine(Path.GetTempPath(), "Artist - Album", "01 Track.flac");

        MetadataFallback.ResolveAlbum("Real Album", path).Should().Be("Real Album");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Track 01")]
    [InlineData("01 - Track")]
    [InlineData("Pista 3")]
    [InlineData("07")]
    [InlineData("Untitled")]
    public void ResolveTrackTitleUsesFileNameForMissingOrGenericTitles(string? taggedTitle)
    {
        var path = Path.Combine(Path.GetTempPath(), "Album", "07 - Real Song.flac");

        MetadataFallback.ResolveTrackTitle(taggedTitle, path).Should().Be("07 - Real Song");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Unknown Album", true)]
    [InlineData("Álbum desconocido", true)]
    [InlineData("Real Album", false)]
    public void IsGenericAlbumClassifiesNames(string? album, bool expected)
    {
        MetadataFallback.IsGenericAlbum(album).Should().Be(expected);
    }

    [Fact]
    public void ResolveTrackTitleKeepsRealTitle()
    {
        var path = Path.Combine(Path.GetTempPath(), "Album", "07.flac");

        MetadataFallback.ResolveTrackTitle("Real Song", path).Should().Be("Real Song");
    }

    [Theory]
    [InlineData("Track of Tears", false)]
    [InlineData("Audio Track 01", true)]
    [InlineData("01 - Track", true)]
    [InlineData("01", true)]
    [InlineData("Sin título", true)]
    public void IsGenericTrackTitleClassifiesNames(string title, bool expected)
    {
        MetadataFallback.IsGenericTrackTitle(title).Should().Be(expected);
    }
}
