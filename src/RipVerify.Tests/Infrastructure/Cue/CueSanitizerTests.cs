namespace RipVerify.Tests.Infrastructure.Cue;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Infrastructure.Cue;
using Xunit;

public class CueSanitizerTests
{
    [Fact]
    public async Task SanitizeReadsAlbumArtistAndTrackTitles()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-cue-");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "01.flac"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(folder.FullName, "02.flac"), string.Empty);
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

            var sanitizer = new CueSanitizer(NullLogger<CueSanitizer>.Instance);
            var cue = await sanitizer.SanitizeAsync(cuePath, CancellationToken.None);

            cue.AlbumTitle.Should().Be("Cue Album");
            cue.Artist.Should().Be("Cue Artist");
            cue.Tracks.Should().HaveCount(2);
            cue.Tracks[0].Title.Should().Be("First Song");
            cue.Tracks[1].Title.Should().Be("Second Song");
            cue.FilePath.Should().Be(cuePath);
            cue.Origin.Should().Be(CueOrigin.Original);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task SanitizeDetectsGeneratedOriginFromFileName()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-cue-");
        try
        {
            var cuePath = Path.Combine(folder.FullName, "album [RipVerify Generated].cue");
            await File.WriteAllTextAsync(cuePath, MinimalCueContent());

            var sanitizer = new CueSanitizer(NullLogger<CueSanitizer>.Instance);
            var cue = await sanitizer.SanitizeAsync(cuePath, CancellationToken.None);

            cue.Origin.Should().Be(CueOrigin.GeneratedFromMetadata);
            cue.FilePath.Should().Be(cuePath);
            cue.RepairNotes.Should().BeEmpty();
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task SanitizeDetectsRepairedOriginFromFileName()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-cue-");
        try
        {
            var cuePath = Path.Combine(folder.FullName, "album [RipVerify Repaired].cue");
            await File.WriteAllTextAsync(cuePath, MinimalCueContent());

            var sanitizer = new CueSanitizer(NullLogger<CueSanitizer>.Instance);
            var cue = await sanitizer.SanitizeAsync(cuePath, CancellationToken.None);

            cue.Origin.Should().Be(CueOrigin.Sanitized);
            cue.FilePath.Should().Be(cuePath);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Theory]
    [InlineData("GeneratedFromItoc")]
    [InlineData("GeneratedFromTocLookup")]
    public async Task SanitizeReadsDeclaredOriginFromRemTag(string declaredOrigin)
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-cue-");
        try
        {
            var cuePath = Path.Combine(folder.FullName, "album.cue");
            await File.WriteAllTextAsync(cuePath, MinimalCueContent(declaredOrigin));

            var sanitizer = new CueSanitizer(NullLogger<CueSanitizer>.Instance);
            var cue = await sanitizer.SanitizeAsync(cuePath, CancellationToken.None);

            cue.Origin.ToString().Should().Be(declaredOrigin);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task SanitizePrefersSpecificRemOriginOverGeneratedSuffix()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-cue-");
        try
        {
            var cuePath = Path.Combine(folder.FullName, "album [RipVerify Generated].cue");
            await File.WriteAllTextAsync(cuePath, MinimalCueContent("GeneratedFromItoc"));

            var sanitizer = new CueSanitizer(NullLogger<CueSanitizer>.Instance);
            var cue = await sanitizer.SanitizeAsync(cuePath, CancellationToken.None);

            cue.Origin.Should().Be(CueOrigin.GeneratedFromItoc);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task SanitizeWritesRepairedCopyAndLeavesOriginalUntouched()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-cue-");
        try
        {
            var audioPath = Path.Combine(folder.FullName, "01.flac");
            await File.WriteAllTextAsync(audioPath, string.Empty);
            var cuePath = Path.Combine(folder.FullName, "album.cue");
            var originalContent =
                "PERFORMER \"Cue Artist\"\r\n" +
                "TITLE \"Cue Album\"\r\n" +
                $"FILE \"{audioPath}\" WAVE\r\n" +
                "  TRACK 01 AUDIO\r\n" +
                "    TITLE \"First Song\"\r\n" +
                "    INDEX 01 00:00:00\r\n";
            await File.WriteAllTextAsync(cuePath, originalContent);

            var sanitizer = new CueSanitizer(NullLogger<CueSanitizer>.Instance);
            var cue = await sanitizer.SanitizeAsync(cuePath, CancellationToken.None);

            var repairedPath = Path.Combine(folder.FullName, "album [RipVerify Repaired].cue");
            cue.FilePath.Should().Be(repairedPath);
            cue.Origin.Should().Be(CueOrigin.Sanitized);
            (await File.ReadAllTextAsync(cuePath)).Should().Be(originalContent);
            File.Exists(repairedPath).Should().BeTrue();

            var repaired = await File.ReadAllTextAsync(repairedPath);
            repaired.Should().StartWith(
                "REM COMMENT \"Generated by RipVerify v0.1.0\"\r\n" +
                "REM RIPVERIFY_CUE_ORIGIN Sanitized\r\n" +
                "REM RIPVERIFY_WAS_REPAIRED true\r\n");
            repaired.Should().Contain("FILE \"01.flac\" WAVE");
        }
        finally
        {
            folder.Delete(true);
        }
    }

    private static string MinimalCueContent(string? declaredOrigin = null)
    {
        var header = declaredOrigin is null
            ? string.Empty
            : $"REM COMMENT \"Generated by RipVerify v0.1.0\"\r\nREM RIPVERIFY_CUE_ORIGIN {declaredOrigin}\r\n";
        return header +
               "PERFORMER \"Cue Artist\"\r\n" +
               "TITLE \"Cue Album\"\r\n" +
               "FILE \"01.flac\" WAVE\r\n" +
               "  TRACK 01 AUDIO\r\n" +
               "    TITLE \"First Song\"\r\n" +
               "    INDEX 01 00:00:00\r\n";
    }
}
