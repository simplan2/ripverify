namespace RipVerify.Tests.Infrastructure.Audio;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using RipVerify.Infrastructure.Audio;
using Xunit;

[Trait("Category", "Integration")]
public sealed class AlbumSpectrumAnalyzerTests : IDisposable
{
    private static readonly SpectrumAnalysisOptions Options = new();
    private static readonly string[] TrackIncompleteWarnings = { "TrackIncomplete" };
    private readonly string _directory;
    private readonly FftSpectrumAnalyzer _analyzer;

    public AlbumSpectrumAnalyzerTests()
    {
        _directory = Directory.CreateTempSubdirectory("ripverify-album-spectrum-").FullName;

        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new AppSettings { FfmpegPath = AudioFixtures.FfmpegPath ?? string.Empty });
        var reader = new FfmpegPcmReader(settings, new FileValidator(NullLogger<FileValidator>.Instance));
        _analyzer = new FftSpectrumAnalyzer(reader);
    }

    [Fact]
    public async Task AlbumWithOneGradualTrack_ReportsWorstTrackAndMinimumNyquist()
    {
        var firstFull = Copy(AudioFixtures.CreateFullSpectrum(_directory), "track-01.flac");
        var gradual = Copy(AudioFixtures.CreateGradualRolloff(_directory), "track-02.flac");
        var secondFull = Copy(firstFull, "track-03.flac");
        var album = Album(("1", firstFull), ("2", gradual), ("3", secondFull));

        var result = await _analyzer.AnalyzeAlbumAsync(album, Options, CancellationToken.None);

        result.BandEnergyDb.Should().NotBeEmpty();
        result.NyquistCutoffHz.Should().BeInRange(14000, 16500);
        result.BrickwallScore.Should().BeLessThan(0.40);
        result.IsBrickwallSuspected.Should().BeFalse();
        result.WorstTrackFilePath.Should().Be(gradual);
    }

    [Fact]
    public async Task AlbumWithOneBrickwallTrack_ReportsWorstTrack()
    {
        var full = Copy(AudioFixtures.CreateFullSpectrum(_directory), "track-01.flac");
        var brickwall = Copy(AudioFixtures.CreateMp3LikeBrickwall(_directory), "track-02.flac");
        var album = Album(("1", full), ("2", brickwall));

        var result = await _analyzer.AnalyzeAlbumAsync(album, Options, CancellationToken.None);

        result.IsBrickwallSuspected.Should().BeTrue();
        result.BrickwallScore.Should().BeGreaterThanOrEqualTo(0.65);
        result.NyquistCutoffHz.Should().BeInRange(14500, 17500);
        result.WorstTrackFilePath.Should().Be(brickwall);
    }

    [Fact]
    public async Task AlbumWithMasteringFilterTrack_DoesNotReportBrickwallButMarksFilter()
    {
        var full = Copy(AudioFixtures.CreateFullSpectrum(_directory), "track-01.flac");
        var masteringFilter = Copy(AudioFixtures.CreateMasteringFilter(_directory), "track-02.flac");
        var album = Album(("1", full), ("2", masteringFilter));

        var result = await _analyzer.AnalyzeAlbumAsync(album, Options, CancellationToken.None);

        result.IsBrickwallSuspected.Should().BeFalse();
        result.BrickwallScore.Should().BeLessThan(0.40);
        result.RawBrickwallScore.Should().BeGreaterThanOrEqualTo(0.65);
        result.MasteringFilterDetected.Should().BeTrue();
        result.WorstTrackFilePath.Should().Be(masteringFilter);
        result.Tracks.Should().Contain(track =>
            track.FilePath == masteringFilter && track.MasteringFilterDetected);
    }

    [Fact]
    public async Task SingleTrackAlbum_ReturnsTrackAnalysisWithoutWorstTrack()
    {
        var full = AudioFixtures.CreateFullSpectrum(_directory);
        var album = Album(("1", full));

        var result = await _analyzer.AnalyzeAlbumAsync(album, Options, CancellationToken.None);

        result.BandEnergyDb.Should().NotBeEmpty();
        result.WorstTrackFilePath.Should().BeNull();
    }

    [Fact]
    public async Task AlbumWithCorruptTrack_StillAnalyzesRemainingTracks()
    {
        var full = Copy(AudioFixtures.CreateFullSpectrum(_directory), "track-01.flac");
        var corrupt = Path.Combine(_directory, "track-02.flac");
        await File.WriteAllTextAsync(corrupt, "not a flac file");
        var album = Album(("1", full), ("2", corrupt));

        var result = await _analyzer.AnalyzeAlbumAsync(album, Options, CancellationToken.None);

        result.BandEnergyDb.Should().NotBeEmpty();
        result.WorstTrackFilePath.Should().Be(full);
    }

    [Fact]
    public async Task AlbumSkipsIncompleteTrackForAggregateSpectrum()
    {
        var firstFull = Copy(AudioFixtures.CreateFullSpectrum(_directory), "track-01.flac");
        var incompleteBrickwall = Copy(AudioFixtures.CreateMp3LikeBrickwall(_directory), "track-02.flac");
        var secondFull = Copy(firstFull, "track-03.flac");
        var album = Album(("1", firstFull), ("2", incompleteBrickwall), ("3", secondFull));
        album = album with
        {
            Tracks = album.Tracks
                .Select(track => track.FilePath == incompleteBrickwall
                    ? track with { IsIncomplete = true, Warnings = TrackIncompleteWarnings }
                    : track)
                .ToList()
        };

        var result = await _analyzer.AnalyzeAlbumAsync(album, Options, CancellationToken.None);

        result.BandEnergyDb.Should().NotBeEmpty();
        result.IsBrickwallSuspected.Should().BeFalse();
        result.WorstTrackFilePath.Should().NotBe(incompleteBrickwall);
        result.NyquistCutoffHz.Should().BeGreaterThan(18000);
    }

    [Fact]
    public async Task AlbumWithOnlyCorruptTracks_ReturnsEmpty()
    {
        var first = Path.Combine(_directory, "track-01.flac");
        var second = Path.Combine(_directory, "track-02.flac");
        await File.WriteAllTextAsync(first, "not a flac file");
        await File.WriteAllTextAsync(second, "also not a flac file");
        var album = Album(("1", first), ("2", second));

        var result = await _analyzer.AnalyzeAlbumAsync(album, Options, CancellationToken.None);

        result.Should().BeEquivalentTo(SpectrumAnalysis.Empty);
        result.WorstTrackFilePath.Should().BeNull();
    }

    private static AlbumMetadata Album(params (string Number, string Path)[] tracks)
        => new()
        {
            Album = "Test Album",
            Artist = "Test Artist",
            Tracks = tracks
                .Select((track, index) => new TrackMetadata
                {
                    Number = index + 1,
                    Title = $"Track {track.Number}",
                    Duration = TimeSpan.FromSeconds(10),
                    FilePath = track.Path
                })
                .ToList()
        };

    private static string Copy(string source, string fileName)
    {
        var destination = Path.Combine(Path.GetDirectoryName(source)!, fileName);
        File.Copy(source, destination, overwrite: true);
        return destination;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
