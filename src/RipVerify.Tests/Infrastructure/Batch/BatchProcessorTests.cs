using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RipVerify.App.Services;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using RipVerify.Core.Services.Classification;
using RipVerify.Infrastructure.Batch;
using RipVerify.Infrastructure.Cue;
using RipVerify.Infrastructure.Reporting;
using System.Diagnostics.CodeAnalysis;
using Xunit;

[module: SuppressMessage("Style", "CA1707:Identifiers should not contain underscores", Justification = "Test method naming convention")]

namespace RipVerify.Tests.Infrastructure.Batch;

[Trait("Category", "Integration")]
public class BatchProcessorTests
{
    private readonly IFolderScanner _folderScanner = Substitute.For<IFolderScanner>();
    private readonly ICueFixerService _cueFixerService = Substitute.For<ICueFixerService>();
    private readonly ICueToolsRunner _cueToolsRunner = Substitute.For<ICueToolsRunner>();
    private readonly ISpectrumAnalyzer _spectrumAnalyzer = Substitute.For<ISpectrumAnalyzer>();
    private readonly IOriginSignalReader _originSignalReader = Substitute.For<IOriginSignalReader>();
    private readonly IVerificationClassifier _classifier = Substitute.For<IVerificationClassifier>();
    private readonly ISettingsService _settingsService = CreateSettingsService();
    private readonly IReportDocumentBuilder _reportBuilder = Substitute.For<IReportDocumentBuilder>();
    private readonly IReportWriter _reportWriter = Substitute.For<IReportWriter>();

    private BatchProcessor CreateProcessor()
    {
        _reportBuilder.Build(
                Arg.Any<AlbumMetadata>(),
                Arg.Any<CueSheet>(),
                Arg.Any<VerificationResult>(),
                Arg.Any<IReadOnlyList<string>>())
            .Returns(new ReportDocument { SchemaVersion = 1 });

        return new BatchProcessor(
            _folderScanner, _cueFixerService, _cueToolsRunner,
            new CueToolsOutputParser(), _spectrumAnalyzer, _originSignalReader,
            _classifier, _settingsService, _reportBuilder, _reportWriter);
    }

    private static ISettingsService CreateSettingsService()
    {
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.Current.Returns(new AppSettings());
        return settingsService;
    }

    [Fact]
    public async Task Batch_ThreeValidInputs_AllCompleted()
    {
        // Arrange
        var processor = CreateProcessor();
        var inputs = new[]
        {
            new ScanInput { Paths = new List<string> { "/album1" } },
            new ScanInput { Paths = new List<string> { "/album2" } },
            new ScanInput { Paths = new List<string> { "/album3" } }
        };

        _folderScanner.ScanAsync(Arg.Any<ScanInput>(), Arg.Any<CancellationToken>())
            .Returns(new ScanResult { Kind = ScanInputKind.AlbumFolder, AudioFilePaths = new List<string> { "/a.flac" } });

        _cueFixerService.FixAsync(Arg.Any<ScanResult>(), Arg.Any<CancellationToken>())
            .Returns(new CueFixResult { Cue = new CueSheet { FilePath = "/album.cue" } });

        _cueToolsRunner.RunAsync(Arg.Any<CueSheet>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CueToolsRunResult { Process = new ProcessResult { ExitCode = 0 } });

        _spectrumAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<SpectrumAnalysisOptions>(), Arg.Any<CancellationToken>())
            .Returns(SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10 } });

        _originSignalReader.ReadAsync(Arg.Any<AlbumMetadata>(), Arg.Any<CancellationToken>())
            .Returns(OriginSignals.Empty);

        _classifier.Classify(Arg.Any<VerificationResult>(), Arg.Any<SpectrumAnalysis>(), Arg.Any<OriginSignals>())
            .Returns(new VerificationResult { Status = VerificationStatus.VerifiedCdRip, StatusIsPreliminary = false });

        var reported = new List<BatchProgress>();
        var progress = new ImmediateProgress<BatchProgress>(reported.Add);

        // Act
        await processor.RunAsync(inputs, progress, CancellationToken.None);

        // Assert
        var final = reported.Last();
        final.IsFinished.Should().BeTrue();
        final.Completed.Should().Be(3);
        final.Failed.Should().Be(0);
        final.Cancelled.Should().Be(0);
        final.Items.Count(i => i.State == BatchItemState.Completed).Should().Be(3);
    }

    [Fact]
    public async Task Batch_OneScannerFailure_ThatItemFailedOthersCompleted()
    {
        var processor = CreateProcessor();
        var inputs = new[]
        {
            new ScanInput { Paths = new List<string> { "/fail" } },
            new ScanInput { Paths = new List<string> { "/ok1" } },
            new ScanInput { Paths = new List<string> { "/ok2" } }
        };

        _folderScanner.ScanAsync(Arg.Any<ScanInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var input = callInfo.ArgAt<ScanInput>(0);
                if (input.Paths[0] == "/fail")
                    return new ScanResult { Kind = ScanInputKind.Unknown, AudioFilePaths = Array.Empty<string>() };
                return new ScanResult { Kind = ScanInputKind.AlbumFolder, AudioFilePaths = new List<string> { "/a.flac" } };
            });

        _cueFixerService.FixAsync(Arg.Any<ScanResult>(), Arg.Any<CancellationToken>())
            .Returns(new CueFixResult { Cue = new CueSheet { FilePath = "/album.cue" } });

        _cueToolsRunner.RunAsync(Arg.Any<CueSheet>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CueToolsRunResult { Process = new ProcessResult { ExitCode = 0 } });

        _spectrumAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<SpectrumAnalysisOptions>(), Arg.Any<CancellationToken>())
            .Returns(SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10 } });

        _originSignalReader.ReadAsync(Arg.Any<AlbumMetadata>(), Arg.Any<CancellationToken>())
            .Returns(OriginSignals.Empty);

        _classifier.Classify(Arg.Any<VerificationResult>(), Arg.Any<SpectrumAnalysis>(), Arg.Any<OriginSignals>())
            .Returns(new VerificationResult { Status = VerificationStatus.VerifiedCdRip, StatusIsPreliminary = false });

        var reported = new List<BatchProgress>();
        var progress = new ImmediateProgress<BatchProgress>(reported.Add);

        await processor.RunAsync(inputs, progress, CancellationToken.None);

        var final = reported.Last();
        final.Failed.Should().Be(1);
        final.Completed.Should().Be(2);
        var failedItem = final.Items.First(i => i.InputPath == "/fail");
        failedItem.State.Should().Be(BatchItemState.Failed);
        failedItem.Errors.Should().Contain("No albums found");
    }

    [Fact]
    public async Task Batch_CueToolsFailure_ExposesProcessError()
    {
        var processor = CreateProcessor();
        var inputs = new[] { new ScanInput { Paths = new List<string> { "/album" } } };

        _folderScanner.ScanAsync(Arg.Any<ScanInput>(), Arg.Any<CancellationToken>())
            .Returns(new ScanResult { Kind = ScanInputKind.AlbumFolder, AudioFilePaths = new List<string> { "/a.flac" } });

        _cueFixerService.FixAsync(Arg.Any<ScanResult>(), Arg.Any<CancellationToken>())
            .Returns(new CueFixResult { Cue = new CueSheet { FilePath = "/album.cue" } });

        _cueToolsRunner.RunAsync(Arg.Any<CueSheet>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CueToolsRunResult
            {
                Process = new ProcessResult
                {
                    ExitCode = 2,
                    StandardError = "The CUE sheet has no tracks to verify."
                }
            });

        var reported = new List<BatchProgress>();
        var progress = new ImmediateProgress<BatchProgress>(reported.Add);

        await processor.RunAsync(inputs, progress, CancellationToken.None);

        var item = reported.Last().Items.Single();
        item.State.Should().Be(BatchItemState.Failed);
        item.Errors.Should().Contain("The CUE sheet has no tracks to verify.");
    }

    [Fact]
    public async Task Batch_ItemCancellation_DoesNotCancelOtherItems()
    {
        var processor = CreateProcessor();
        var inputs = new[]
        {
            new ScanInput { Paths = new List<string> { "/cancel" } },
            new ScanInput { Paths = new List<string> { "/ok" } }
        };

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _folderScanner.ScanAsync(Arg.Any<ScanInput>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var input = callInfo.ArgAt<ScanInput>(0);
                if (input.Paths[0] == "/cancel")
                {
                    started.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, callInfo.ArgAt<CancellationToken>(1));
                }

                return new ScanResult
                {
                    Kind = ScanInputKind.AlbumFolder,
                    AudioFilePaths = new List<string> { "/a.flac" }
                };
            });

        _cueFixerService.FixAsync(Arg.Any<ScanResult>(), Arg.Any<CancellationToken>())
            .Returns(new CueFixResult { Cue = new CueSheet { FilePath = "/album.cue" } });
        _cueToolsRunner.RunAsync(Arg.Any<CueSheet>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CueToolsRunResult { Process = new ProcessResult { ExitCode = 0 } });
        _spectrumAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<SpectrumAnalysisOptions>(), Arg.Any<CancellationToken>())
            .Returns(SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10 } });
        _originSignalReader.ReadAsync(Arg.Any<AlbumMetadata>(), Arg.Any<CancellationToken>())
            .Returns(OriginSignals.Empty);
        _classifier.Classify(Arg.Any<VerificationResult>(), Arg.Any<SpectrumAnalysis>(), Arg.Any<OriginSignals>())
            .Returns(new VerificationResult { Status = VerificationStatus.VerifiedCdRip });

        var reported = new List<BatchProgress>();
        var progress = new ImmediateProgress<BatchProgress>(reported.Add);
        using var itemCts = new CancellationTokenSource();
        var itemTokens = new Dictionary<string, CancellationToken>(StringComparer.OrdinalIgnoreCase)
        {
            ["/cancel"] = itemCts.Token
        };

        var runTask = processor.RunAsync(inputs, progress, CancellationToken.None, itemTokens);
        await started.Task;
        itemCts.Cancel();
        await runTask;

        var final = reported.Last();
        final.IsFinished.Should().BeTrue();
        final.Cancelled.Should().Be(1);
        final.Completed.Should().Be(1);
        final.Items.Single(i => i.InputPath == "/cancel").State.Should().Be(BatchItemState.Cancelled);
        final.Items.Single(i => i.InputPath == "/ok").State.Should().Be(BatchItemState.Completed);
    }

    [Fact]
    public async Task Batch_Cancellation_MarksRemainingCancelled()
    {
        var processor = CreateProcessor();
        var cts = new CancellationTokenSource();
        var inputs = new[]
        {
            new ScanInput { Paths = new List<string> { "/album1" } },
            new ScanInput { Paths = new List<string> { "/album2" } },
            new ScanInput { Paths = new List<string> { "/album3" } }
        };

        // First scan succeeds, then cancel
        _folderScanner.ScanAsync(Arg.Any<ScanInput>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var input = callInfo.ArgAt<ScanInput>(0);
                if (input.Paths[0] == "/album1")
                    return new ScanResult { Kind = ScanInputKind.AlbumFolder, AudioFilePaths = new List<string> { "/a.flac" } };
                cts.Cancel();
                throw new OperationCanceledException();
            });

        var reported = new List<BatchProgress>();
        var progress = new ImmediateProgress<BatchProgress>(reported.Add);

        await processor.RunAsync(inputs, progress, cts.Token);

        var final = reported.Last();
        final.Cancelled.Should().BeGreaterThan(0);
        final.IsFinished.Should().BeTrue();
    }

    [Fact]
    public async Task Batch_CompletedItem_WritesJsonReport()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-batch-report-");
        try
        {
            var album = new AlbumMetadata { Album = "Album", Artist = "Artist" };
            var cue = new CueSheet { FilePath = Path.Combine(folder.FullName, "album.cue") };

            _folderScanner.ScanAsync(Arg.Any<ScanInput>(), Arg.Any<CancellationToken>())
                .Returns(new ScanResult
                {
                    Kind = ScanInputKind.AlbumFolder,
                    AudioFilePaths = new List<string> { Path.Combine(folder.FullName, "a.flac") },
                    Album = album
                });
            _cueFixerService.FixAsync(Arg.Any<ScanResult>(), Arg.Any<CancellationToken>())
                .Returns(new CueFixResult { Cue = cue });
            _cueToolsRunner.RunAsync(Arg.Any<CueSheet>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CueToolsRunResult { Process = new ProcessResult { ExitCode = 0 } });
            _originSignalReader.ReadAsync(Arg.Any<AlbumMetadata>(), Arg.Any<CancellationToken>())
                .Returns(OriginSignals.Empty);
            _classifier.Classify(Arg.Any<VerificationResult>(), Arg.Any<SpectrumAnalysis>(), Arg.Any<OriginSignals>())
                .Returns(new VerificationResult
                {
                    AlbumPath = folder.FullName,
                    Status = VerificationStatus.VerifiedCdRip
                });

            var writer = new ReportWriter(
                new MarkdownReportRenderer(new LocalizationService()),
                NullLogger<ReportWriter>.Instance);
            var processor = new BatchProcessor(
                _folderScanner, _cueFixerService, _cueToolsRunner,
                new CueToolsOutputParser(), _spectrumAnalyzer, _originSignalReader,
                _classifier, _settingsService, new ReportDocumentBuilder(), writer);

            await processor.RunAsync(
                new[] { new ScanInput { Paths = new List<string> { folder.FullName } } },
                new ImmediateProgress<BatchProgress>(_ => { }),
                CancellationToken.None);

            File.Exists(Path.Combine(folder.FullName, ReportWriter.JsonFileName)).Should().BeTrue();
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task Batch_CompletedItem_ExposesScanMetadata()
    {
        var processor = CreateProcessor();
        var album = new AlbumMetadata
        {
            Album = "Nadie Nos Va A Detener",
            Artist = "Salsa Kids",
            Tracks = new List<TrackMetadata> { new() { Number = 1, Title = "Mi Otro Yo" } }
        };

        _folderScanner.ScanAsync(Arg.Any<ScanInput>(), Arg.Any<CancellationToken>())
            .Returns(new ScanResult
            {
                Kind = ScanInputKind.AlbumFolder,
                AudioFilePaths = new List<string> { "/a.flac" },
                Album = album
            });
        _cueFixerService.FixAsync(Arg.Any<ScanResult>(), Arg.Any<CancellationToken>())
            .Returns(new CueFixResult { Cue = new CueSheet { FilePath = "/album.cue" } });
        _cueToolsRunner.RunAsync(Arg.Any<CueSheet>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CueToolsRunResult { Process = new ProcessResult { ExitCode = 0 } });
        _spectrumAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<SpectrumAnalysisOptions>(), Arg.Any<CancellationToken>())
            .Returns(SpectrumAnalysis.Empty with { BandEnergyDb = new List<double> { -10 } });
        _originSignalReader.ReadAsync(Arg.Any<AlbumMetadata>(), Arg.Any<CancellationToken>())
            .Returns(OriginSignals.Empty);
        _classifier.Classify(Arg.Any<VerificationResult>(), Arg.Any<SpectrumAnalysis>(), Arg.Any<OriginSignals>())
            .Returns(new VerificationResult { Status = VerificationStatus.VerifiedCdRip });

        var reported = new List<BatchProgress>();
        var progress = new ImmediateProgress<BatchProgress>(reported.Add);

        await processor.RunAsync(
            new[] { new ScanInput { Paths = new List<string> { "/album" } } },
            progress,
            CancellationToken.None);

        var item = reported.Last().Items.Single();
        item.Scan.Should().NotBeNull();
        item.Scan!.Album.Should().NotBeNull();
        item.Scan.Album!.Album.Should().Be("Nadie Nos Va A Detener");
        item.Scan.Album.Artist.Should().Be("Salsa Kids");
        item.Scan.Album.Tracks.Should().HaveCount(1);
    }

    [Fact]
    public async Task Batch_EmptyInputs_FinishedImmediately()
    {
        var processor = CreateProcessor();
        var reported = new List<BatchProgress>();
        var progress = new ImmediateProgress<BatchProgress>(reported.Add);

        await processor.RunAsync(Array.Empty<ScanInput>(), progress, CancellationToken.None);

        reported.Should().NotBeEmpty();
        var final = reported.Last();
        final.IsFinished.Should().BeTrue();
        final.Total.Should().Be(0);
    }

    private sealed class ImmediateProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
