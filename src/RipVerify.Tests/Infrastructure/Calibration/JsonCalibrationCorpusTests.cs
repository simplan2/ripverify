namespace RipVerify.Tests.Infrastructure.Calibration;

using System.Text.Json;
using FluentAssertions;
using RipVerify.Core.Models;
using RipVerify.Infrastructure.Calibration;
using Xunit;

public sealed class JsonCalibrationCorpusTests : IDisposable
{
    private readonly string _directory;
    private readonly string _manifestPath;

    public JsonCalibrationCorpusTests()
    {
        _directory = Directory.CreateTempSubdirectory("ripverify-calibration-").FullName;
        _manifestPath = Path.Combine(_directory, "manifest.json");
    }

    [Fact]
    public async Task LoadAsync_ReadsNewFieldsAndExcludesUnknownCategory()
    {
        await File.WriteAllTextAsync(
            _manifestPath,
            """
            {
              "Entries": [
                {
                  "FilePath": "audio/cd-natural-rolloff.flac",
                  "ExpectedOrigin": "cd-natural-rolloff",
                  "ObservedBrickwallScore": 0.42,
                  "ObservedNyquistCutoffHz": 15800,
                  "ObservedRolloffSlopeDbPerKHz": 2.5,
                  "ObservedSpectralFlatness": 0.35,
                  "Notes": "Serie Millennium 21",
                  "TaggedAt": "2026-11-02T00:00:00Z"
                },
                {
                  "FilePath": "audio/vhs.flac",
                  "ExpectedOrigin": "vhs",
                  "ObservedBrickwallScore": 0.99,
                  "ObservedNyquistCutoffHz": 16000
                }
              ]
            }
            """);

        var corpus = await new JsonCalibrationCorpus(_manifestPath).LoadAsync(CancellationToken.None);

        corpus.Entries.Should().HaveCount(1);
        var entry = corpus.Entries[0];
        entry.ExpectedOrigin.Should().Be(CalibrationEntry.CdNaturalRolloffOrigin);
        entry.ObservedRolloffSlopeDbPerKHz.Should().BeApproximately(2.5, 0.001);
        entry.ObservedSpectralFlatness.Should().BeApproximately(0.35, 0.001);
        entry.ObservedNyquistCutoffHz.Should().BeApproximately(15800, 0.001);
        entry.Notes.Should().Be("Serie Millennium 21");
    }

    [Fact]
    public async Task SaveEntryAsync_PersistsNewFields()
    {
        var repository = new JsonCalibrationCorpus(_manifestPath);
        var entry = new CalibrationEntry
        {
            FilePath = "audio/transcode.flac",
            ExpectedOrigin = CalibrationEntry.TranscodeOrigin,
            ObservedBrickwallScore = 0.91,
            ObservedNyquistCutoffHz = 16000,
            ObservedRolloffSlopeDbPerKHz = 18.5,
            ObservedSpectralFlatness = 0.98,
            Notes = "Known MP3 transcode",
            TaggedAt = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)
        };

        await repository.SaveEntryAsync(entry, CancellationToken.None);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(_manifestPath));
        var saved = json.RootElement.GetProperty("Entries")[0];
        saved.GetProperty("ExpectedOrigin").GetString().Should().Be(CalibrationEntry.TranscodeOrigin);
        saved.GetProperty("ObservedRolloffSlopeDbPerKHz").GetDouble().Should().BeApproximately(18.5, 0.001);
        saved.GetProperty("ObservedSpectralFlatness").GetDouble().Should().BeApproximately(0.98, 0.001);
        saved.GetProperty("ObservedNyquistCutoffHz").GetDouble().Should().BeApproximately(16000, 0.001);
    }

    [Fact]
    public async Task LoadAsync_MissingManifest_ReturnsEmptyCorpus()
    {
        var corpus = await new JsonCalibrationCorpus(Path.Combine(_directory, "missing.json"))
            .LoadAsync(CancellationToken.None);

        corpus.Entries.Should().BeEmpty();
        corpus.CalibratedBrickwallThreshold.Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
