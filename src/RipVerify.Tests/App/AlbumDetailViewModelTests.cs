namespace RipVerify.Tests.App;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RipVerify.App.Services;
using RipVerify.App.ViewModels;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using RipVerify.Infrastructure.Cue;
using RipVerify.Infrastructure.Logs;
using RipVerify.Infrastructure.Reporting;
using Xunit;

public class AlbumDetailViewModelTests
{
    [Fact]
    public async Task VerifyAgainWritesJsonReport()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-detail-");
        try
        {
            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(new AppSettings());
            var runner = Substitute.For<ICueToolsRunner>();
            runner.RunAsync(Arg.Any<CueSheet>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CueToolsRunResult { Process = new ProcessResult { ExitCode = 0 } });
            var classifier = Substitute.For<IVerificationClassifier>();
            classifier.Classify(Arg.Any<VerificationResult>(), Arg.Any<SpectrumAnalysis>(), Arg.Any<OriginSignals>())
                .Returns(new VerificationResult
                {
                    AlbumPath = folder.FullName,
                    Status = VerificationStatus.VerifiedCdRip,
                    ConfidenceScore = 4
                });
            var origin = Substitute.For<IOriginSignalReader>();
            origin.ReadAsync(Arg.Any<AlbumMetadata>(), Arg.Any<CancellationToken>())
                .Returns(OriginSignals.Empty);

            var viewModel = CreateViewModel(settings, runner, classifier, origin, out _);
            var cue = new CueSheet { FilePath = Path.Combine(folder.FullName, "album.cue") };
            viewModel.Load(new VerificationResult(), new AlbumMetadata(), cue);

            await viewModel.VerifyAgainCommand.ExecuteAsync(null);

            var jsonPath = Path.Combine(folder.FullName, ReportWriter.JsonFileName);
            File.Exists(jsonPath).Should().BeTrue();
            viewModel.IsNotificationVisible.Should().BeTrue();
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task VerifyAgainUsesCueAudioWhenAlbumTracksAreMissing()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-detail-");
        try
        {
            var audioPath = Path.Combine(folder.FullName, "01.wav");
            await File.WriteAllTextAsync(audioPath, string.Empty);
            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(new AppSettings());
            var runner = Substitute.For<ICueToolsRunner>();
            runner.RunAsync(Arg.Any<CueSheet>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CueToolsRunResult { Process = new ProcessResult { ExitCode = 0 } });
            var spectrumAnalyzer = Substitute.For<ISpectrumAnalyzer>();
            spectrumAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<SpectrumAnalysisOptions>(), Arg.Any<CancellationToken>())
                .Returns(SpectrumAnalysis.Empty with { NyquistCutoffHz = 22050, BandEnergyDb = new List<double> { -20 } });
            var classifier = Substitute.For<IVerificationClassifier>();
            classifier.Classify(Arg.Any<VerificationResult>(), Arg.Any<SpectrumAnalysis>(), Arg.Any<OriginSignals>())
                .Returns(callInfo => new VerificationResult
                {
                    AlbumPath = folder.FullName,
                    Status = VerificationStatus.VerifiedCdRip,
                    Spectrum = callInfo.ArgAt<SpectrumAnalysis>(1)
                });
            var origin = Substitute.For<IOriginSignalReader>();
            origin.ReadAsync(Arg.Any<AlbumMetadata>(), Arg.Any<CancellationToken>())
                .Returns(OriginSignals.Empty);

            var viewModel = CreateViewModel(settings, runner, classifier, origin, out _, spectrumAnalyzer);
            var cue = new CueSheet
            {
                FilePath = Path.Combine(folder.FullName, "album.cue"),
                Tracks = new[] { new CueTrack { Number = 1, FileName = "01.wav" } }
            };
            viewModel.Load(new VerificationResult(), new AlbumMetadata(), cue);

            await viewModel.VerifyAgainCommand.ExecuteAsync(null);

            await spectrumAnalyzer.Received(1).AnalyzeAsync(
                audioPath,
                Arg.Any<SpectrumAnalysisOptions>(),
                Arg.Any<CancellationToken>());
            var json = await File.ReadAllTextAsync(Path.Combine(folder.FullName, ReportWriter.JsonFileName));
            json.Should().Contain("\"nyquistCutoffHz\": 22050");
            json.Should().Contain("\"bandEnergyDb\"");
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task ExportMarkdownWritesReportInForcedEnglish()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-detail-");
        try
        {
            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(new AppSettings());
            var viewModel = CreateViewModel(
                settings,
                Substitute.For<ICueToolsRunner>(),
                Substitute.For<IVerificationClassifier>(),
                Substitute.For<IOriginSignalReader>(),
                out var localization);
            localization.SetLanguage("es");

            var result = new VerificationResult
            {
                AlbumPath = folder.FullName,
                Status = VerificationStatus.LikelyStreaming,
                Log = new RipLogVerification
                {
                    LogPath = Path.Combine(folder.FullName, "album.log"),
                    Warnings = new[] { RipLogVerifier.VirtualDriveWarning }
                }
            };
            var cue = new CueSheet { FilePath = Path.Combine(folder.FullName, "album.cue") };
            var cueWarnings = new[] { "Estructura de CUE inusual." };
            viewModel.Load(
                result,
                new AlbumMetadata { Album = "Album", Artist = "Artist" },
                cue,
                cueWarnings);
            viewModel.ForceEnglish = true;

            await viewModel.ExportMarkdownCommand.ExecuteAsync(null);

            var markdownPath = Path.Combine(folder.FullName, ReportWriter.MarkdownFileName);
            File.Exists(markdownPath).Should().BeTrue();
            var text = await File.ReadAllTextAsync(markdownPath);
            text.Should().Contain("Verified at");
            text.Should().Contain("Streaming Lossless");
            text.Should().Contain("Warnings");
            text.Should().Contain(RipLogVerifier.VirtualDriveWarning);
            text.Should().Contain("Estructura de CUE inusual.");
            viewModel.IsNotificationVisible.Should().BeTrue();
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public void LoadShowsAlbumFolderSeparateFromCueFile()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-detail-");
        try
        {
            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(new AppSettings());
            var viewModel = CreateViewModel(
                settings,
                Substitute.For<ICueToolsRunner>(),
                Substitute.For<IVerificationClassifier>(),
                Substitute.For<IOriginSignalReader>(),
                out _);
            var cuePath = Path.Combine(folder.FullName, "album.cue");

            viewModel.Load(
                new VerificationResult { AlbumPath = cuePath },
                new AlbumMetadata(),
                new CueSheet { FilePath = cuePath });

            viewModel.AlbumPathText.Should().Contain(folder.FullName);
            viewModel.AlbumPathText.Should().NotContain("album.cue");
            viewModel.CuePathText.Should().Contain("album.cue");
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public void LoadShowsPlatformIsrcAndIncompleteTrackWarning()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new AppSettings());
        var viewModel = CreateViewModel(
            settings,
            Substitute.For<ICueToolsRunner>(),
            Substitute.For<IVerificationClassifier>(),
            Substitute.For<IOriginSignalReader>(),
            out var localization);
        localization.SetLanguage("es");
        var album = new AlbumMetadata
        {
            Album = "Greatest Hits",
            Artist = "Debbie Gibson",
            Tracks = new[]
            {
                new TrackMetadata { Number = 1, Title = "Only in My Dreams", Duration = TimeSpan.FromMinutes(4) },
                new TrackMetadata
                {
                    Number = 7,
                    Title = "Shake Your Love",
                    Duration = TimeSpan.FromSeconds(30),
                    IsIncomplete = true,
                    Warnings = new[] { "TrackIncomplete" }
                }
            }
        };
        var result = new VerificationResult
        {
            Status = VerificationStatus.LikelyStreaming,
            Origin = new OriginSignals
            {
                HasPlatformIsrc = true,
                PlatformIsrcExample = "USAT29902070",
                StreamingConfidence = 0.7
            }
        };

        viewModel.Load(result, album, new CueSheet());

        viewModel.HasPlatformIsrc.Should().BeTrue();
        viewModel.PlatformIsrcText.Should().Be("Metadatos de plataforma detectados (ISRC: USAT29902070)");
        viewModel.HasTracks.Should().BeTrue();
        viewModel.TrackRows.Should().ContainSingle(row =>
            row.Number == 7 &&
            row.IsIncomplete &&
            row.WarningText == "Pista incompleta o corrupta");
    }

    [Fact]
    public void LoadShowsMetadataFallbackWarning()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new AppSettings());
        var viewModel = CreateViewModel(
            settings,
            Substitute.For<ICueToolsRunner>(),
            Substitute.For<IVerificationClassifier>(),
            Substitute.For<IOriginSignalReader>(),
            out var localization);
        localization.SetLanguage("es");

        viewModel.Load(
            new VerificationResult(),
            new AlbumMetadata
            {
                Album = "Carpeta del álbum",
                Tracks = new[] { new TrackMetadata { Number = 1, Title = "01 Track" } },
                MetadataFallbackUsed = true
            },
            new CueSheet());

        viewModel.HasMetadataWarning.Should().BeTrue();
        viewModel.MetadataWarningText.Should().Contain("Faltan algunas etiquetas");
    }

    [Fact]
    public void LoadShowsMissingMetadataWarningWhenAlbumCannotBeRead()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new AppSettings());
        var viewModel = CreateViewModel(
            settings,
            Substitute.For<ICueToolsRunner>(),
            Substitute.For<IVerificationClassifier>(),
            Substitute.For<IOriginSignalReader>(),
            out var localization);
        localization.SetLanguage("es");

        viewModel.Load(new VerificationResult(), new AlbumMetadata(), new CueSheet());

        viewModel.HasMetadataWarning.Should().BeTrue();
        viewModel.MetadataWarningText.Should().Contain("No se pudieron leer los metadatos");
    }

    [Fact]
    public void LoadBuildsSpectrumAreaAndPerTrackStatuses()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new AppSettings());
        var viewModel = CreateViewModel(
            settings,
            Substitute.For<ICueToolsRunner>(),
            Substitute.For<IVerificationClassifier>(),
            Substitute.For<IOriginSignalReader>(),
            out var localization);
        localization.SetLanguage("es");

        var album = new AlbumMetadata
        {
            Album = "Spectrum",
            Tracks = new[]
            {
                new TrackMetadata { Number = 1, Title = "Clean", FilePath = @"C:\music\01.wav" },
                new TrackMetadata { Number = 2, Title = "Low", FilePath = @"C:\music\02.wav" }
            }
        };
        var result = new VerificationResult
        {
            Status = VerificationStatus.AuthenticRipWithLog,
            Spectrum = new SpectrumAnalysis
            {
                NyquistCutoffHz = 22050,
                BandEnergyDb = new[] { -10.0, -20.0, -30.0 },
                BandEdgesHz = new[] { 1000.0, 10000.0, 22050.0 },
                Tracks = new[]
                {
                    new SpectrumTrackAnalysis
                    {
                        FilePath = @"C:\music\01.wav",
                        NyquistCutoffHz = 22050
                    },
                    new SpectrumTrackAnalysis
                    {
                        FilePath = @"C:\music\02.wav",
                        NyquistCutoffHz = 16000,
                        IsBrickwallSuspected = true
                    }
                }
            },
            Log = new RipLogVerification
            {
                LogPath = @"C:\music\album.log",
                DriveName = "HL-DT-STBD-RE WH14NS40",
                LogTrackCount = 2,
                ComparedTracks = 2,
                MatchedTracks = 2
            }
        };

        var cueWarnings = new[] { "Estructura de CUE inusual." };
        viewModel.Load(
            result,
            album,
            new CueSheet(),
            cueWarnings);

        viewModel.SpectrumAreaData.Should().NotBeNull();
        viewModel.SpectrumLineData.Should().NotBeNull();
        viewModel.SpectrumStartLabel.Should().Be("20 Hz");
        viewModel.SpectrumEndLabel.Should().Be("22 kHz");
        viewModel.TrackRows.Should().Contain(row => row.Number == 1 && row.StatusText == "✅ OK");
        viewModel.TrackRows.Should().Contain(row => row.Number == 2 && row.StatusText == "⚠️ Espectro Bajo");
        viewModel.HasLog.Should().BeTrue();
        viewModel.LogStatusText.Should().Contain("2 de 2");
        viewModel.HasCueWarnings.Should().BeTrue();
    }

    [Fact]
    public void LoadShowsMasteringFilterMessageAndKeepsTrackOk()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new AppSettings());
        var viewModel = CreateViewModel(
            settings,
            Substitute.For<ICueToolsRunner>(),
            Substitute.For<IVerificationClassifier>(),
            Substitute.For<IOriginSignalReader>(),
            out var localization);
        localization.SetLanguage("es");

        var album = new AlbumMetadata
        {
            Album = "Mastering Filter",
            Tracks = new[]
            {
                new TrackMetadata { Number = 1, Title = "Filtered", FilePath = @"C:\music\01.wav" }
            }
        };
        var result = new VerificationResult
        {
            Status = VerificationStatus.AuthenticRipWithLog,
            Spectrum = new SpectrumAnalysis
            {
                NyquistCutoffHz = 19746,
                BrickwallScore = 0.3,
                RawBrickwallScore = 1.0,
                IsBrickwallSuspected = false,
                MasteringFilterDetected = true,
                BandEnergyDb = new[] { -10.0, -20.0, -30.0 },
                BandEdgesHz = new[] { 1000.0, 10000.0, 22050.0 },
                Tracks = new[]
                {
                    new SpectrumTrackAnalysis
                    {
                        FilePath = @"C:\music\01.wav",
                        NyquistCutoffHz = 19746,
                        BrickwallScore = 0.3,
                        RawBrickwallScore = 1.0,
                        IsBrickwallSuspected = false,
                        MasteringFilterDetected = true
                    }
                }
            }
        };

        viewModel.Load(result, album, new CueSheet(), Array.Empty<string>());

        viewModel.IsSpectrumAvailable.Should().BeTrue();
        viewModel.IsSpectrumSuspected.Should().BeFalse();
        viewModel.IsSpectrumClean.Should().BeFalse();
        viewModel.IsMasteringFilterDetected.Should().BeTrue();
        viewModel.MasteringFilterText.Should().Be("Corte de filtro de masterización (no es MP3).");
        viewModel.TrackRows.Should().ContainSingle();
        viewModel.TrackRows[0].StatusText.Should().Be(localization.GetString("Track_StatusOk"));
    }

    [Fact]
    public async Task VerifyAgainIncludesLogVerificationBeforeClassification()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-detail-");
        try
        {
            var audioPath = Path.Combine(folder.FullName, "01.wav");
            await File.WriteAllBytesAsync(audioPath, new byte[] { 1, 2, 3 });

            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(new AppSettings());
            var runner = Substitute.For<ICueToolsRunner>();
            runner.RunAsync(Arg.Any<CueSheet>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new CueToolsRunResult { Process = new ProcessResult { ExitCode = 0 } });
            var spectrumAnalyzer = Substitute.For<ISpectrumAnalyzer>();
            spectrumAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<SpectrumAnalysisOptions>(), Arg.Any<CancellationToken>())
                .Returns(SpectrumAnalysis.Empty with { NyquistCutoffHz = 22050, BandEnergyDb = new List<double> { -20 } });
            var logVerifier = Substitute.For<IRipLogVerifier>();
            logVerifier.VerifyAsync(
                    Arg.Any<AlbumMetadata>(),
                    Arg.Any<string?>(),
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<CancellationToken>())
                .Returns(new RipLogVerification
                {
                    LogPath = "album.log",
                    LogTrackCount = 1,
                    ComparedTracks = 1,
                    MatchedTracks = 1
                });
            VerificationResult? classified = null;
            var classifier = Substitute.For<IVerificationClassifier>();
            classifier.Classify(Arg.Any<VerificationResult>(), Arg.Any<SpectrumAnalysis>(), Arg.Any<OriginSignals>())
                .Returns(callInfo =>
                {
                    classified = callInfo.ArgAt<VerificationResult>(0);
                    return classified with
                    {
                        AlbumPath = folder.FullName,
                        Status = VerificationStatus.AuthenticRipWithLog
                    };
                });

            var viewModel = CreateViewModel(
                settings,
                runner,
                classifier,
                Substitute.For<IOriginSignalReader>(),
                out _,
                spectrumAnalyzer,
                logVerifier);
            var album = new AlbumMetadata
            {
                Album = "Album",
                Tracks = new[] { new TrackMetadata { Number = 1, FilePath = audioPath } },
                DbPoweramp = new DbPowerampEvidence
                {
                    AccurateRipDiscId = "00142e60-00bd0bfe-950acb0c",
                    AccurateRipResult = "Accurate (confidence 12)",
                    IsAccurate = true
                }
            };
            var cue = new CueSheet
            {
                FilePath = Path.Combine(folder.FullName, "album.cue"),
                Origin = CueOrigin.GeneratedFromMetadata
            };
            viewModel.Load(new VerificationResult(), album, cue);

            await viewModel.VerifyAgainCommand.ExecuteAsync(null);

            classified.Should().NotBeNull();
            classified!.Log.AllTracksMatched.Should().BeTrue();
            classified.CueOrigin.Should().Be(CueOrigin.GeneratedFromMetadata);
            classified.DbPoweramp.IsAccurate.Should().BeTrue();
            await logVerifier.Received(1).VerifyAsync(
                Arg.Any<AlbumMetadata>(),
                folder.FullName,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public async Task StopAsyncCancelsRunningVerification()
    {
        var folder = Directory.CreateTempSubdirectory("ripverify-detail-");

        try
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<CueToolsRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken submittedToken = default;
            var settings = Substitute.For<ISettingsService>();
            settings.Current.Returns(new AppSettings());
            var runner = Substitute.For<ICueToolsRunner>();
            runner.RunAsync(
                    Arg.Any<CueSheet>(),
                    Arg.Any<string>(),
                    Arg.Do<CancellationToken>(token =>
                    {
                        submittedToken = token;
                        started.TrySetResult();
                    }))
                .Returns(release.Task);

            var viewModel = CreateViewModel(
                settings,
                runner,
                Substitute.For<IVerificationClassifier>(),
                Substitute.For<IOriginSignalReader>(),
                out _);
            var cue = new CueSheet { FilePath = Path.Combine(folder.FullName, "album.cue") };
            viewModel.Load(new VerificationResult(), new AlbumMetadata(), cue);

            var verifyTask = viewModel.VerifyAgainCommand.ExecuteAsync(null);
            await started.Task;
            viewModel.IsBusy.Should().BeTrue();

            var stopTask = viewModel.StopAsync();
            submittedToken.IsCancellationRequested.Should().BeTrue();
            release.TrySetResult(new CueToolsRunResult
            {
                Process = new ProcessResult { WasCancelled = true }
            });

            await stopTask;
            try
            {
                await verifyTask;
            }
            catch (OperationCanceledException)
            {
            }

            viewModel.IsBusy.Should().BeFalse();
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact]
    public void CloseCommandRaisesCloseRequested()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new AppSettings());
        var viewModel = CreateViewModel(
            settings,
            Substitute.For<ICueToolsRunner>(),
            Substitute.For<IVerificationClassifier>(),
            Substitute.For<IOriginSignalReader>(),
            out _);
        var closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        viewModel.CloseCommand.Execute(null);

        closed.Should().BeTrue();
    }

    [Fact]
    public void LoadExposesCueOriginDbPowerampAndParserWarnings()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new AppSettings());
        var viewModel = CreateViewModel(
            settings,
            Substitute.For<ICueToolsRunner>(),
            Substitute.For<IVerificationClassifier>(),
            Substitute.For<IOriginSignalReader>(),
            out var localization);
        localization.SetLanguage("es");

        var result = new VerificationResult
        {
            Status = VerificationStatus.VerifiedWithWarnings,
            CueOrigin = CueOrigin.GeneratedFromMetadata,
            DbPoweramp = new DbPowerampEvidence
            {
                AccurateRipDiscId = "00142e60-00bd0bfe-950acb0c",
                AccurateRipResult = "Accurate (confidence 12)",
                IsAccurate = true
            },
            ParserWarnings = new[] { "2 | (42/173) Differs in 58 samples", "informational line" },
            Diagnostics = new[] { "Classifier_CtdbVerifiedWithArMismatch" }
        };

        viewModel.Load(
            result,
            new AlbumMetadata(),
            new CueSheet { Origin = CueOrigin.GeneratedFromMetadata });

        viewModel.StatusIconText.Should().Be("⚠");
        viewModel.HasCueOriginWarning.Should().BeTrue();
        viewModel.CueOriginText.Should().Be("Estructura del Disco (CUE): Generado desde metadatos");
        viewModel.CueOriginWarningText.Should().Be(
            "⚠️ La alineación de las pistas es aproximada, la verificación en bases de datos puede no ser exacta.");
        viewModel.HasDbPowerampVerified.Should().BeTrue();
        viewModel.ParserWarnings.Should().HaveCount(2);
        viewModel.CriticalParserWarnings.Should().ContainSingle()
            .Which.Should().Contain("Differs");
        viewModel.Diagnostics.Should().ContainSingle()
            .Which.Should().Contain("CTDB valida");
    }

    private static AlbumDetailViewModel CreateViewModel(
        ISettingsService settings,
        ICueToolsRunner runner,
        IVerificationClassifier classifier,
        IOriginSignalReader origin,
        out LocalizationService localization,
        ISpectrumAnalyzer? spectrumAnalyzer = null,
        IRipLogVerifier? logVerifier = null)
    {
        localization = new LocalizationService();
        var writer = new ReportWriter(
            new MarkdownReportRenderer(localization),
            NullLogger<ReportWriter>.Instance);

        return new AlbumDetailViewModel(
            runner,
            new CueToolsOutputParser(),
            spectrumAnalyzer ?? Substitute.For<ISpectrumAnalyzer>(),
            origin,
            classifier,
            settings,
            localization,
            new ReportDocumentBuilder(),
            writer,
            logVerifier);
    }
}
