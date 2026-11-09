using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Infrastructure.Cue;

namespace RipVerify.Tests.Infrastructure.Cue;

public class CueFixerServiceTests
{
    private static readonly byte[] WavBytes = { 1, 2, 3 };
    private static readonly string[] SanitizedRepairNotes = { "Absolute path converted to relative" };

    private const string UnusualWarning =
        "Estructura de CUE inusual. El audio puede ser válido, pero el archivo podría ser problemático en otros reproductores.";

    [Fact]
    public async Task MissingReferencedAudioAddsUnusualStructureWarning()
    {
        using var temp = new TempDirectory();
        var cuePath = Path.Combine(temp.Path, "album.cue");
        await File.WriteAllTextAsync(cuePath, "FILE \"missing.wav\" WAVE");

        var sanitizer = Substitute.For<ICueSanitizer>();
        sanitizer.SanitizeAsync(cuePath, Arg.Any<CancellationToken>())
            .Returns(new CueSheet
            {
                FilePath = cuePath,
                Tracks = new[]
                {
                    new CueTrack { Number = 1, FileName = "missing.wav", Start = TimeSpan.Zero }
                }
            });

        var service = new CueFixerService(
            Substitute.For<IFolderScanner>(),
            sanitizer,
            Substitute.For<ICueGenerator>(),
            Substitute.For<ICueMetadataReader>(),
            NullLogger<CueFixerService>.Instance);

        var result = await service.FixAsync(
            new ScanResult
            {
                Kind = ScanInputKind.AlbumFolder,
                CuePath = cuePath,
                AudioFilePaths = new[] { Path.Combine(temp.Path, "missing.wav") }
            },
            CancellationToken.None);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().Contain(UnusualWarning);
    }

    [Fact]
    public async Task ValidReferencedAudioDoesNotAddUnusualStructureWarning()
    {
        using var temp = new TempDirectory();
        var cuePath = Path.Combine(temp.Path, "album.cue");
        var wavPath = Path.Combine(temp.Path, "track.wav");
        await File.WriteAllTextAsync(cuePath, "FILE \"track.wav\" WAVE");
        await File.WriteAllBytesAsync(wavPath, WavBytes);

        var sanitizer = Substitute.For<ICueSanitizer>();
        sanitizer.SanitizeAsync(cuePath, Arg.Any<CancellationToken>())
            .Returns(new CueSheet
            {
                FilePath = cuePath,
                Tracks = new[]
                {
                    new CueTrack { Number = 1, FileName = "track.wav", Start = TimeSpan.Zero }
                }
            });

        var service = new CueFixerService(
            Substitute.For<IFolderScanner>(),
            sanitizer,
            Substitute.For<ICueGenerator>(),
            Substitute.For<ICueMetadataReader>(),
            NullLogger<CueFixerService>.Instance);

        var result = await service.FixAsync(
            new ScanResult
            {
                Kind = ScanInputKind.AlbumFolder,
                CuePath = cuePath,
                AudioFilePaths = new[] { wavPath }
            },
            CancellationToken.None);

        result.Warnings.Should().NotContain(UnusualWarning);
    }

    [Fact]
    public async Task PropagatesSanitizedCueOrigin()
    {
        using var temp = new TempDirectory();
        var cuePath = Path.Combine(temp.Path, "album.cue");
        var wavPath = Path.Combine(temp.Path, "track.wav");
        await File.WriteAllTextAsync(cuePath, "FILE \"track.wav\" WAVE");
        await File.WriteAllBytesAsync(wavPath, WavBytes);

        var sanitizer = Substitute.For<ICueSanitizer>();
        sanitizer.SanitizeAsync(cuePath, Arg.Any<CancellationToken>())
            .Returns(new CueSheet
            {
                FilePath = cuePath,
                Origin = CueOrigin.Sanitized,
                RepairNotes = SanitizedRepairNotes,
                Tracks = new[]
                {
                    new CueTrack { Number = 1, FileName = "track.wav", Start = TimeSpan.Zero }
                }
            });

        var service = new CueFixerService(
            Substitute.For<IFolderScanner>(),
            sanitizer,
            Substitute.For<ICueGenerator>(),
            Substitute.For<ICueMetadataReader>(),
            NullLogger<CueFixerService>.Instance);

        var result = await service.FixAsync(
            new ScanResult
            {
                Kind = ScanInputKind.AlbumFolder,
                CuePath = cuePath,
                AudioFilePaths = new[] { wavPath }
            },
            CancellationToken.None);

        result.Origin.Should().Be(CueOrigin.Sanitized);
    }

    [Fact]
    public async Task PropagatesGeneratedCueOrigin()
    {
        using var temp = new TempDirectory();
        var trackPath = Path.Combine(temp.Path, "track.wav");
        await File.WriteAllBytesAsync(trackPath, WavBytes);

        var generator = Substitute.For<ICueGenerator>();
        generator.GenerateAsync(Arg.Any<AlbumMetadata>(), Arg.Any<CancellationToken>())
            .Returns(new CueSheet
            {
                Origin = CueOrigin.GeneratedFromMetadata,
                WasGenerated = true,
                Tracks = new[]
                {
                    new CueTrack { Number = 1, FileName = "track.wav", Start = TimeSpan.Zero }
                }
            });

        var service = new CueFixerService(
            Substitute.For<IFolderScanner>(),
            Substitute.For<ICueSanitizer>(),
            generator,
            Substitute.For<ICueMetadataReader>(),
            NullLogger<CueFixerService>.Instance);

        var generatedPath = Path.Combine(temp.Path, "Test Album [RipVerify Generated].cue");
        await File.WriteAllTextAsync(generatedPath, "stale content");

        var result = await service.FixAsync(
            new ScanResult
            {
                Kind = ScanInputKind.AlbumFolder,
                AlbumFolder = temp.Path,
                Album = new AlbumMetadata
                {
                    Album = "Test Album",
                    Tracks = new[]
                    {
                        new TrackMetadata { Number = 1, Title = "Track", FilePath = trackPath, Duration = TimeSpan.FromSeconds(10) }
                    }
                }
            },
            CancellationToken.None);

        result.Origin.Should().Be(CueOrigin.GeneratedFromMetadata);
        result.Cue.Should().NotBeNull();
        result.Cue!.FilePath.Should().Be(generatedPath);

        var content = await File.ReadAllTextAsync(generatedPath);
        content.Should().StartWith(
            "REM COMMENT \"Generated by RipVerify v0.1.0\"\r\n" +
            "REM RIPVERIFY_CUE_ORIGIN GeneratedFromMetadata\r\n");
        content.Should().Contain("TITLE \"Test Album\"");
        content.Should().NotContain("stale content");
    }

    [Fact]
    public async Task GeneratedItocCueIncludesSourceMarker()
    {
        using var temp = new TempDirectory();
        var trackPath = Path.Combine(temp.Path, "track.wav");
        await File.WriteAllBytesAsync(trackPath, WavBytes);

        var generator = Substitute.For<ICueGenerator>();
        generator.GenerateAsync(Arg.Any<AlbumMetadata>(), Arg.Any<CancellationToken>())
            .Returns(new CueSheet
            {
                Origin = CueOrigin.GeneratedFromItoc,
                WasGenerated = true,
                Tracks = new[]
                {
                    new CueTrack { Number = 1, FileName = "track.wav", Start = TimeSpan.Zero }
                }
            });

        var service = new CueFixerService(
            Substitute.For<IFolderScanner>(),
            Substitute.For<ICueSanitizer>(),
            generator,
            Substitute.For<ICueMetadataReader>(),
            NullLogger<CueFixerService>.Instance);

        await service.FixAsync(
            new ScanResult
            {
                Kind = ScanInputKind.AlbumFolder,
                AlbumFolder = temp.Path,
                Album = new AlbumMetadata
                {
                    Album = "Itoc Album",
                    Tracks = new[]
                    {
                        new TrackMetadata { Number = 1, Title = "Track", FilePath = trackPath, Duration = TimeSpan.FromSeconds(10) }
                    }
                }
            },
            CancellationToken.None);

        var content = await File.ReadAllTextAsync(Path.Combine(temp.Path, "Itoc Album [RipVerify Generated].cue"));
        content.Should().Contain("REM RIPVERIFY_CUE_ORIGIN GeneratedFromItoc\r\n");
        content.Should().Contain("REM RIPVERIFY_SOURCE_ITOC true\r\n");
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "RipVerifyTests-" + Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
