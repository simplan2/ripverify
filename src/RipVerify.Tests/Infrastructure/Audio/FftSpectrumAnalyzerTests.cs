namespace RipVerify.Tests.Infrastructure.Audio;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using RipVerify.Infrastructure.Audio;
using Xunit;

[Trait("Category", "Integration")]
public sealed class FftSpectrumAnalyzerTests : IDisposable
{
    private static readonly SpectrumAnalysisOptions Options = new();
    private readonly string _directory;
    private readonly FftSpectrumAnalyzer _analyzer;

    public FftSpectrumAnalyzerTests()
    {
        _directory = Directory.CreateTempSubdirectory("ripverify-spectrum-").FullName;

        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new AppSettings { FfmpegPath = AudioFixtures.FfmpegPath ?? string.Empty });
        var reader = new FfmpegPcmReader(settings, new FileValidator(NullLogger<FileValidator>.Instance));
        _analyzer = new FftSpectrumAnalyzer(reader);
    }

    [Fact]
    public async Task FullSpectrum22kHz_IsNotBrickwall()
    {
        var path = AudioFixtures.CreateFullSpectrum(_directory);

        var result = await _analyzer.AnalyzeAsync(path, Options, CancellationToken.None);

        result.BandEnergyDb.Should().NotBeEmpty();
        result.NyquistCutoffHz.Should().BeGreaterThan(20000);
        result.BrickwallScore.Should().BeLessThan(0.40);
        result.IsBrickwallSuspected.Should().BeFalse();
    }

    [Fact]
    public async Task GradualRolloff15kHz_IsNotFlaggedAsBrickwall()
    {
        var path = AudioFixtures.CreateGradualRolloff(_directory);

        var result = await _analyzer.AnalyzeAsync(path, Options, CancellationToken.None);

        result.BandEnergyDb.Should().NotBeEmpty();
        result.NyquistCutoffHz.Should().BeInRange(14000, 16500);
        result.BrickwallScore.Should().BeLessThan(0.40);
        result.IsBrickwallSuspected.Should().BeFalse();
        result.WorstTrackFilePath.Should().BeNull();
    }

    [Fact]
    public async Task Mp3LikeBrickwall16kHz_IsFlaggedAsBrickwall()
    {
        var path = AudioFixtures.CreateMp3LikeBrickwall(_directory);

        var result = await _analyzer.AnalyzeAsync(path, Options, CancellationToken.None);

        result.BandEnergyDb.Should().NotBeEmpty();
        result.NyquistCutoffHz.Should().BeInRange(14500, 17500);
        result.BrickwallScore.Should().BeGreaterThanOrEqualTo(0.65);
        result.IsBrickwallSuspected.Should().BeTrue();
    }

    [Fact]
    public async Task MasteringFilterAt19700Hz_IsNotFlaggedAsBrickwall()
    {
        var path = AudioFixtures.CreateMasteringFilter(_directory);

        var result = await _analyzer.AnalyzeAsync(path, Options, CancellationToken.None);

        result.BandEnergyDb.Should().NotBeEmpty();
        result.NyquistCutoffHz.Should().BeInRange(18000, 20500);
        result.RawBrickwallScore.Should().BeGreaterThanOrEqualTo(0.65);
        result.BrickwallScore.Should().BeLessThan(0.40);
        result.BrickwallScore.Should().BeApproximately(
            result.RawBrickwallScore * SpectrumThresholds.HighCutoffPenaltyMultiplier,
            0.001);
        result.MasteringFilterDetected.Should().BeTrue();
        result.IsBrickwallSuspected.Should().BeFalse();
        result.Tracks.Should().ContainSingle();
        result.Tracks[0].MasteringFilterDetected.Should().BeTrue();
    }

    [Theory]
    [InlineData(40.0, 15.0, 1.0, 1.0)]
    [InlineData(0.0, 0.0, 0.0, 0.0)]
    [InlineData(5.0, 0.1, 0.3, 0.1245)]
    [InlineData(80.0, 30.0, 1.5, 1.0)]
    public void ComputeBrickwallScore_UsesFixedWeights(
        double energyDropDb,
        double rolloffSlopeDbPerKHz,
        double flatnessAfterCutoff,
        double expected)
    {
        var score = FftSpectrumAnalyzer.ComputeBrickwallScore(
            energyDropDb,
            rolloffSlopeDbPerKHz,
            flatnessAfterCutoff);

        score.Should().BeApproximately(expected, 0.001);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
