namespace RipVerify.Tests.Infrastructure.Audio;

using FluentAssertions;
using RipVerify.Infrastructure.Audio;
using Xunit;

[Trait("Category", "Integration")]
public sealed class TagLibMetadataReaderTests : IDisposable
{
    private readonly string _directory;
    private readonly TagLibMetadataReader _reader = new();

    public TagLibMetadataReaderTests()
    {
        _directory = Directory.CreateTempSubdirectory("ripverify-tags-").FullName;
    }

    [Fact]
    public async Task ReadsAccurateRipTagsFromFlac()
    {
        var path = AudioFixtures.CreateFullSpectrum(_directory);
        SetTags(path, "00142e60-00bd0bfe-950acb0c", "Accurate (confidence 12)");

        var metadata = await _reader.ReadAlbumMetadataAsync(new[] { path }, CancellationToken.None);

        metadata.DbPoweramp.AccurateRipDiscId.Should().Be("00142e60-00bd0bfe-950acb0c");
        metadata.DbPoweramp.AccurateRipResult.Should().Be("Accurate (confidence 12)");
        metadata.DbPoweramp.IsAccurate.Should().BeTrue();
        metadata.DbPoweramp.Tracks.Should().ContainSingle();
    }

    [Fact]
    public async Task OneInaccurateFileInvalidatesTheAlbumTagEvidence()
    {
        var first = Copy(AudioFixtures.CreateFullSpectrum(_directory), "track-01.flac");
        var second = Copy(first, "track-02.flac");
        SetTags(first, "00142e60-00bd0bfe-950acb0c", "Accurate (confidence 12)");
        SetTags(second, "00142e60-00bd0bfe-950acb0c", "Inaccurate (confidence 2)");

        var metadata = await _reader.ReadAlbumMetadataAsync(new[] { first, second }, CancellationToken.None);

        metadata.DbPoweramp.IsAccurate.Should().BeFalse();
        metadata.DbPoweramp.Tracks.Should().HaveCount(2);
        metadata.DbPoweramp.Tracks.Should().Contain(track => track.IsAccurate);
        metadata.DbPoweramp.Tracks.Should().Contain(track => !track.IsAccurate);
    }

    [Fact]
    public async Task FilesWithoutDatabaseEntryDoNotBlockOtherFiles()
    {
        var first = Copy(AudioFixtures.CreateFullSpectrum(_directory), "track-01.flac");
        var second = Copy(first, "track-02.flac");
        SetTags(first, "00142e60-00bd0bfe-950acb0c", "Accurate (confidence 12)");
        SetTags(second, null, "Not in database");

        var metadata = await _reader.ReadAlbumMetadataAsync(new[] { first, second }, CancellationToken.None);

        metadata.DbPoweramp.IsAccurate.Should().BeTrue();
    }

    private static void SetTags(string path, string? discId, string? result)
    {
        using var file = TagLib.File.Create(path);
        var tag = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph, true);
        if (!string.IsNullOrWhiteSpace(discId))
        {
            tag.SetField("ACCURATERIPDISCID", new[] { discId });
        }

        if (!string.IsNullOrWhiteSpace(result))
        {
            tag.SetField("ACCURATERIPRESULT", new[] { result });
        }

        file.Save();
    }

    private static string Copy(string source, string fileName)
    {
        var destination = Path.Combine(Path.GetDirectoryName(source)!, fileName);
        File.Copy(source, destination, overwrite: true);
        return destination;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
