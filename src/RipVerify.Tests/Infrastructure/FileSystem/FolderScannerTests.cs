namespace RipVerify.Tests.Infrastructure.FileSystem;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Infrastructure.Cue;
using RipVerify.Infrastructure.FileSystem;
using Xunit;

public class FolderScannerTests
{
    [Fact]
    public async Task ScanDirectoryWithCueAndMultipleAudio_PopulatesAlbumMetadata()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-scan-");
        try
        {
            var cuePath = Path.Combine(folder.FullName, "album.cue");
            await File.WriteAllTextAsync(cuePath, "REM test");
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "01.wav"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "02.wav"), string.Empty);

            var metadata = new AlbumMetadata
            {
                Album = "Test Album",
                Artist = "Test Artist",
                Tracks = new[]
                {
                    new TrackMetadata { Number = 1, Title = "One", FilePath = Path.Combine(folder.FullName, "01.wav") },
                    new TrackMetadata { Number = 2, Title = "Two", FilePath = Path.Combine(folder.FullName, "02.wav") }
                }
            };
            var reader = Substitute.For<ICueMetadataReader>();
            reader.ReadAlbumMetadataAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(metadata);

            var scanner = new FolderScanner(reader);
            var result = await scanner.ScanAsync(
                new ScanInput { Paths = new[] { folder.FullName } },
                CancellationToken.None);

            result.Kind.Should().Be(ScanInputKind.AlbumFolder);
            result.CuePath.Should().Be(cuePath);
            result.AudioFilePaths.Should().HaveCount(2);
            result.Album.Should().BeSameAs(metadata);
            await reader.Received(1).ReadAlbumMetadataAsync(
                Arg.Is<IReadOnlyList<string>>(files => files.Count == 2),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task ScanDirectory_MarksTrackShorterThanHalfTheAlbumAverageAsIncomplete()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-scan-");
        try
        {
            var first = Path.Combine(folder.FullName, "01.flac");
            var second = Path.Combine(folder.FullName, "02.flac");
            var third = Path.Combine(folder.FullName, "03.flac");
            await File.WriteAllTextAsync(first, string.Empty);
            await File.WriteAllTextAsync(second, string.Empty);
            await File.WriteAllTextAsync(third, string.Empty);

            var metadata = new AlbumMetadata
            {
                Album = "Test Album",
                Artist = "Test Artist",
                Tracks = new[]
                {
                    new TrackMetadata { Number = 1, Title = "One", Duration = TimeSpan.FromMinutes(4), FilePath = first },
                    new TrackMetadata { Number = 2, Title = "Two", Duration = TimeSpan.FromSeconds(30), FilePath = second },
                    new TrackMetadata { Number = 3, Title = "Three", Duration = TimeSpan.FromMinutes(4), FilePath = third }
                }
            };
            var reader = Substitute.For<ICueMetadataReader>();
            reader.ReadAlbumMetadataAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(metadata);

            var scanner = new FolderScanner(reader);
            var result = await scanner.ScanAsync(
                new ScanInput { Paths = new[] { folder.FullName } },
                CancellationToken.None);

            result.Album.Should().NotBeNull();
            result.Album!.Tracks[0].IsIncomplete.Should().BeFalse();
            result.Album.Tracks[1].IsIncomplete.Should().BeTrue();
            result.Album.Tracks[1].Warnings.Should().Contain("TrackIncomplete");
            result.Album.Tracks[2].IsIncomplete.Should().BeFalse();
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task ScanDirectoryWithCue_UsesCueTitlesOverGenericTags()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-scan-");
        try
        {
            var first = Path.Combine(folder.FullName, "01.flac");
            var second = Path.Combine(folder.FullName, "02.flac");
            await File.WriteAllTextAsync(first, string.Empty);
            await File.WriteAllTextAsync(second, string.Empty);
            var cuePath = Path.Combine(folder.FullName, "album.cue");
            await File.WriteAllTextAsync(
                cuePath,
                """
                PERFORMER "Cue Artist"
                TITLE "Cue Album"
                FILE "01.flac" WAVE
                  TRACK 01 AUDIO
                    TITLE "First Song"
                    INDEX 01 00:00:00
                FILE "02.flac" WAVE
                  TRACK 02 AUDIO
                    TITLE "Second Song"
                    INDEX 01 00:00:00
                """);

            var metadata = new AlbumMetadata
            {
                Album = "Tag Album",
                Artist = "Tag Artist",
                MetadataFallbackUsed = true,
                Tracks = new[]
                {
                    new TrackMetadata { Number = 1, Title = "Track 01", FilePath = first },
                    new TrackMetadata { Number = 2, Title = "Track 02", FilePath = second }
                }
            };
            var reader = Substitute.For<ICueMetadataReader>();
            reader.ReadAlbumMetadataAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(metadata);

            var scanner = new FolderScanner(reader, new CueSanitizer(NullLogger<CueSanitizer>.Instance));
            var result = await scanner.ScanAsync(
                new ScanInput { Paths = new[] { folder.FullName } },
                CancellationToken.None);

            result.Album.Should().NotBeNull();
            result.Album!.Album.Should().Be("Cue Album");
            result.Album.Artist.Should().Be("Cue Artist");
            result.Album.Tracks.Select(track => track.Title).Should().Equal("First Song", "Second Song");
            result.Album.MetadataFallbackUsed.Should().BeFalse();
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task ScanLooseFilesWithoutReadableTags_UsesFolderNameAsAlbum()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-scan-");
        try
        {
            var first = Path.Combine(folder.FullName, "01.flac");
            var second = Path.Combine(folder.FullName, "02.flac");
            await File.WriteAllTextAsync(first, string.Empty);
            await File.WriteAllTextAsync(second, string.Empty);

            var metadata = new AlbumMetadata
            {
                Album = string.Empty,
                Artist = string.Empty,
                MetadataFallbackUsed = true,
                Tracks = new[]
                {
                    new TrackMetadata { Number = 1, Title = "01", FilePath = first },
                    new TrackMetadata { Number = 2, Title = "02", FilePath = second }
                }
            };
            var reader = Substitute.For<ICueMetadataReader>();
            reader.ReadAlbumMetadataAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(metadata);

            var scanner = new FolderScanner(reader);
            var result = await scanner.ScanAsync(
                new ScanInput { Paths = new[] { first, second } },
                CancellationToken.None);

            result.Kind.Should().Be(ScanInputKind.LooseFiles);
            result.Album.Should().NotBeNull();
            result.Album!.Album.Should().Be(folder.Name);
            result.Album.MetadataFallbackUsed.Should().BeTrue();
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task ScanDirectoryWithCueAndSingleImage_PopulatesAlbumMetadata()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-scan-");
        try
        {
            var cuePath = Path.Combine(folder.FullName, "album.cue");
            await File.WriteAllTextAsync(cuePath, "REM test");
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "image.wav"), string.Empty);

            var metadata = new AlbumMetadata { Album = "Image Album", Artist = "Image Artist" };
            var reader = Substitute.For<ICueMetadataReader>();
            reader.ReadAlbumMetadataAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(metadata);

            var scanner = new FolderScanner(reader);
            var result = await scanner.ScanAsync(
                new ScanInput { Paths = new[] { folder.FullName } },
                CancellationToken.None);

            result.Kind.Should().Be(ScanInputKind.SingleImageWithCue);
            result.CuePath.Should().Be(cuePath);
            result.Album.Should().BeSameAs(metadata);
        }
        finally
        {
            folder.Delete(true);
        }
    }
}
