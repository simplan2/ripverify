namespace RipVerify.Tests.Infrastructure.Reporting;

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RipVerify.App.Services;
using RipVerify.Core.Models;
using RipVerify.Infrastructure.Logs;
using RipVerify.Infrastructure.Reporting;
using Xunit;

public class ReportWriterTests
{
    [Fact]
    public async Task WritesJsonWithExpectedSchemaFields()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-report-");
        try
        {
            var writer = CreateWriter(out _);
            var document = CreateDocument(folder.FullName);

            await writer.WriteJsonAsync(folder.FullName, document, CancellationToken.None);

            var path = Path.Combine(folder.FullName, ReportWriter.JsonFileName);
            File.Exists(path).Should().BeTrue();

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var root = json.RootElement;
            root.GetProperty("schemaVersion").GetInt32().Should().Be(ReportWriter.CurrentSchemaVersion);
            root.GetProperty("app").GetString().Should().Be("RipVerify");
            root.GetProperty("albumPath").GetString().Should().Be(folder.FullName);
            root.GetProperty("results")[0].GetProperty("status").GetString().Should().Be("VerifiedWithWarnings");
            root.GetProperty("results")[0].GetProperty("cueOrigin").GetString().Should().Be("GeneratedFromMetadata");
            root.GetProperty("results")[0].GetProperty("dbPoweramp").GetProperty("isAccurate").GetBoolean().Should().BeTrue();
            root.GetProperty("results")[0].GetProperty("parserWarnings")[0].GetString().Should().Contain("Differs");
            root.GetProperty("results")[0].GetProperty("ctdbStatus").GetString().Should().Be("AccuratelyRipped");
            root.GetProperty("results")[0].GetProperty("ctdbConfidenceScore").GetInt32().Should().Be(11);
            root.GetProperty("results")[0]
                .GetProperty("log")
                .GetProperty("logPath")
                .GetString()
                .Should()
                .Be(Path.Combine(folder.FullName, "album.log"));
            root.GetProperty("results")[0]
                .GetProperty("log")
                .GetProperty("warnings")[0]
                .GetString()
                .Should()
                .Be(RipLogVerifier.VirtualDriveWarning);
            root.GetProperty("results")[0]
                .GetProperty("cueWarnings")[0]
                .GetString()
                .Should()
                .Be("Estructura de CUE inusual.");
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task MarkdownHonorsForceEnglish()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-report-");
        try
        {
            var writer = CreateWriter(out var localization);
            localization.SetLanguage("es");
            var document = CreateDocument(folder.FullName);

            await writer.WriteMarkdownAsync(folder.FullName, document, forceEnglish: true, CancellationToken.None);
            var english = await File.ReadAllTextAsync(Path.Combine(folder.FullName, ReportWriter.MarkdownFileName));
            english.Should().Contain("Verified at");
            english.Should().Contain("album.log");
            english.Should().Contain("Warnings");
            english.Should().Contain(RipLogVerifier.VirtualDriveWarning);
            english.Should().Contain("Estructura de CUE inusual.");
            english.Should().Contain("Disc structure");
            english.Should().Contain("dBPoweramp tag");
            english.Should().Contain("Differs in 58 samples");

            await writer.WriteMarkdownAsync(folder.FullName, document, forceEnglish: false, CancellationToken.None);
            var spanish = await File.ReadAllTextAsync(Path.Combine(folder.FullName, ReportWriter.MarkdownFileName));
            spanish.Should().Contain("Verificado el");
            spanish.Should().Contain("Advertencias");
            spanish.Should().Contain(RipLogVerifier.VirtualDriveWarning);
            spanish.Should().Contain("Estructura de CUE inusual.");
            spanish.Should().Contain("Estructura del Disco");
            spanish.Should().Contain("Tag dBPoweramp");
            spanish.Should().Contain("Differs in 58 samples");
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task UnsupportedSchemaIsNotWritten()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-report-");
        try
        {
            var writer = CreateWriter(out _);
            var document = CreateDocument(folder.FullName) with { SchemaVersion = 99 };

            await writer.WriteJsonAsync(folder.FullName, document, CancellationToken.None);
            await writer.WriteMarkdownAsync(folder.FullName, document, forceEnglish: false, CancellationToken.None);

            File.Exists(Path.Combine(folder.FullName, ReportWriter.JsonFileName)).Should().BeFalse();
            File.Exists(Path.Combine(folder.FullName, ReportWriter.MarkdownFileName)).Should().BeFalse();
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task MissingFolderDoesNotThrow()
    {
        var writer = CreateWriter(out _);
        var document = CreateDocument(@"C:\does-not-exist");

        var jsonAction = async () => await writer.WriteJsonAsync(@"C:\does-not-exist", document, CancellationToken.None);
        var markdownAction = async () => await writer.WriteMarkdownAsync(@"C:\does-not-exist", document, forceEnglish: false, CancellationToken.None);

        await jsonAction.Should().NotThrowAsync();
        await markdownAction.Should().NotThrowAsync();
    }

    private static ReportWriter CreateWriter(out LocalizationService localization)
    {
        localization = new LocalizationService();
        return new ReportWriter(
            new MarkdownReportRenderer(localization),
            NullLogger<ReportWriter>.Instance);
    }

    private static ReportDocument CreateDocument(string albumPath)
    {
        return new ReportDocument
        {
            SchemaVersion = ReportWriter.CurrentSchemaVersion,
            AppVersion = "0.1.0",
            VerifiedAt = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero),
            AlbumPath = albumPath,
            Album = new AlbumMetadata { Album = "Test Album", Artist = "Test Artist" },
            Results = new[]
            {
                new ReportResultEntry
                {
                    AlbumPath = albumPath,
                    CuePath = Path.Combine(albumPath, "album.cue"),
                    CueOrigin = CueOrigin.GeneratedFromMetadata,
                    DbPoweramp = new DbPowerampEvidence
                    {
                        AccurateRipDiscId = "00142e60-00bd0bfe-950acb0c",
                        AccurateRipResult = "Accurate (confidence 12)",
                        IsAccurate = true
                    },
                    AccurateRipStatus = AccurateRipStatus.NotFound,
                    ConfidenceScore = 10,
                    CtdbStatus = CtdbStatus.AccuratelyRipped,
                    CtdbConfidenceScore = 11,
                    Status = VerificationStatus.VerifiedWithWarnings,
                    CueWarnings = new[] { "Estructura de CUE inusual." },
                    ParserWarnings = new[] { "2 | (42/173) Differs in 58 samples" },
                    Log = new RipLogVerification
                    {
                        LogPath = Path.Combine(albumPath, "album.log"),
                        DriveName = "Test Drive",
                        LogTrackCount = 1,
                        ExpectedTrackCount = 1,
                        ComparedTracks = 1,
                        MatchedTracks = 1,
                        Warnings = new[] { RipLogVerifier.VirtualDriveWarning }
                    }
                }
            }
        };
    }
}
