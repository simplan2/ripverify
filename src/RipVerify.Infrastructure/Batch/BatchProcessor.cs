using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using RipVerify.Infrastructure.Cue;
using Serilog;

namespace RipVerify.Infrastructure.Batch;

public sealed class BatchProcessor : IBatchProcessor
{
    private readonly IFolderScanner _folderScanner;
    private readonly ICueFixerService _cueFixerService;
    private readonly ICueToolsRunner _cueToolsRunner;
    private readonly CueToolsOutputParser _cueToolsParser;
    private readonly ISpectrumAnalyzer _spectrumAnalyzer;
    private readonly IOriginSignalReader _originSignalReader;
    private readonly IVerificationClassifier _classifier;
    private readonly ISettingsService _settingsService;
    private readonly IReportDocumentBuilder _reportBuilder;
    private readonly IReportWriter _reportWriter;
    private readonly IRipLogVerifier? _ripLogVerifier;

    public BatchProcessor(
        IFolderScanner folderScanner,
        ICueFixerService cueFixerService,
        ICueToolsRunner cueToolsRunner,
        CueToolsOutputParser cueToolsParser,
        ISpectrumAnalyzer spectrumAnalyzer,
        IOriginSignalReader originSignalReader,
        IVerificationClassifier classifier,
        ISettingsService settingsService,
        IReportDocumentBuilder reportBuilder,
        IReportWriter reportWriter,
        IRipLogVerifier? ripLogVerifier = null)
    {
        _folderScanner = folderScanner;
        _cueFixerService = cueFixerService;
        _cueToolsRunner = cueToolsRunner;
        _cueToolsParser = cueToolsParser;
        _spectrumAnalyzer = spectrumAnalyzer;
        _originSignalReader = originSignalReader;
        _classifier = classifier;
        _settingsService = settingsService;
        _reportBuilder = reportBuilder;
        _reportWriter = reportWriter;
        _ripLogVerifier = ripLogVerifier;
    }

    public async Task RunAsync(
        IReadOnlyList<ScanInput> inputs,
        IProgress<BatchProgress> progress,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, CancellationToken>? itemCancellationTokens = null)
    {
        var items = new List<BatchItem>();
        foreach (var input in inputs)
        {
            var path = input.Paths.Count > 0 ? input.Paths[0] : string.Empty;
            items.Add(new BatchItem
            {
                InputPath = path,
                DisplayName = System.IO.Path.GetFileName(path) ?? path,
                State = BatchItemState.Pending,
                StartedAt = DateTimeOffset.UtcNow
            });
        }

        int total = items.Count;
        int completed = 0;
        int failed = 0;
        int cancelled = 0;

        ReportProgress(progress, total, completed, failed, cancelled, items, null, false);

        for (int i = 0; i < items.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                for (int j = i; j < items.Count; j++)
                {
                    items[j] = items[j] with
                    {
                        State = BatchItemState.Cancelled,
                        CompletedAt = DateTimeOffset.UtcNow
                    };
                    cancelled++;
                }
                ReportProgress(progress, total, completed, failed, cancelled, items, null, true);
                break;
            }

            var current = items[i];
            var itemToken = CancellationToken.None;
            if (itemCancellationTokens is not null &&
                itemCancellationTokens.TryGetValue(current.InputPath, out var requestedToken))
            {
                itemToken = requestedToken;
            }

            using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, itemToken);
            var linkedToken = itemCts.Token;

            if (linkedToken.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    for (int j = i; j < items.Count; j++)
                    {
                        items[j] = items[j] with
                        {
                            State = BatchItemState.Cancelled,
                            CompletedAt = DateTimeOffset.UtcNow
                        };
                        cancelled++;
                    }
                    ReportProgress(progress, total, completed, failed, cancelled, items, null, true);
                    break;
                }

                current = current with { State = BatchItemState.Cancelled, CompletedAt = DateTimeOffset.UtcNow };
                items[i] = current;
                cancelled++;
                ReportProgress(progress, total, completed, failed, cancelled, items, current, false);
                continue;
            }

            try
            {
                current = current with { State = BatchItemState.Scanning };
                items[i] = current;
                ReportProgress(progress, total, completed, failed, cancelled, items, current, false);

                var scanInput = inputs[i];
                var scanResult = await _folderScanner.ScanAsync(scanInput, linkedToken);
                linkedToken.ThrowIfCancellationRequested();
                current = current with { Scan = scanResult };
                items[i] = current;

                if (scanResult.Kind == ScanInputKind.Unknown || scanResult.AudioFilePaths.Count == 0)
                {
                    IReadOnlyList<string> scanErrors = scanResult.Errors.Count > 0
                        ? scanResult.Errors
                        : new[] { "No albums found" };
                    Log.Warning("Scan failed for {InputPath}: {Errors}", current.InputPath, string.Join(" | ", scanErrors));
                    current = current with { State = BatchItemState.Failed, Errors = scanErrors, CompletedAt = DateTimeOffset.UtcNow };
                    items[i] = current;
                    failed++;
                    ReportProgress(progress, total, completed, failed, cancelled, items, current, false);
                    continue;
                }

                current = current with { State = BatchItemState.FixingCue };
                items[i] = current;
                ReportProgress(progress, total, completed, failed, cancelled, items, current, false);

                var cueFixResult = await _cueFixerService.FixAsync(scanResult, linkedToken);
                linkedToken.ThrowIfCancellationRequested();
                current = current with { CueFix = cueFixResult };

                if (cueFixResult.Cue == null)
                {
                    IReadOnlyList<string> cueErrors = cueFixResult.Errors.Count > 0
                        ? cueFixResult.Errors
                        : new[] { "Failed to fix or generate CUE" };
                    Log.Warning("CUE preparation failed for {InputPath}: {Errors}", current.InputPath, string.Join(" | ", cueErrors));
                    current = current with { State = BatchItemState.Failed, Errors = cueErrors, CompletedAt = DateTimeOffset.UtcNow };
                    items[i] = current;
                    failed++;
                    ReportProgress(progress, total, completed, failed, cancelled, items, current, false);
                    continue;
                }

                current = current with { State = BatchItemState.VerifyingCueTools };
                items[i] = current;
                ReportProgress(progress, total, completed, failed, cancelled, items, current, false);

                var audioDir = System.IO.Path.GetDirectoryName(cueFixResult.Cue.FilePath) ?? string.Empty;
                var runResult = await _cueToolsRunner.RunAsync(cueFixResult.Cue, audioDir, linkedToken);
                linkedToken.ThrowIfCancellationRequested();

                if (runResult.Process.WasCancelled)
                {
                    throw new OperationCanceledException(linkedToken);
                }

                if (!runResult.Succeeded)
                {
                    var detail = FirstMeaningfulLine(runResult.Process.StandardError)
                        ?? FirstMeaningfulLine(runResult.Process.StandardOutput)
                        ?? $"CUETools exited with code {runResult.Process.ExitCode}";
                    Log.Warning("CUETools verification failed for {InputPath}: {Error}", current.InputPath, detail);
                    current = current with
                    {
                        State = BatchItemState.Failed,
                        Errors = new[] { detail },
                        CompletedAt = DateTimeOffset.UtcNow
                    };
                    items[i] = current;
                    failed++;
                    ReportProgress(progress, total, completed, failed, cancelled, items, current, false);
                    continue;
                }

                var partialResult = CueToolsOutputParser.Parse(runResult, cueFixResult.Cue, _settingsService.Current) with
                {
                    CueOrigin = cueFixResult.Origin,
                    DbPoweramp = scanResult.Album?.DbPoweramp ?? DbPowerampEvidence.Empty
                };

                current = current with { State = BatchItemState.AnalyzingSpectrum };
                items[i] = current;
                ReportProgress(progress, total, completed, failed, cancelled, items, current, false);

                SpectrumAnalysis spectrum = SpectrumAnalysis.Empty;
                try
                {
                    var audioFiles = scanResult.AudioFilePaths
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (audioFiles.Count > 1)
                    {
                        var albumForSpectrum = BuildSpectrumAlbum(scanResult, audioFiles);
                        spectrum = await _spectrumAnalyzer.AnalyzeAlbumAsync(
                            albumForSpectrum,
                            new SpectrumAnalysisOptions(),
                            linkedToken) ?? SpectrumAnalysis.Empty;
                    }
                    else if (audioFiles.Count == 1)
                    {
                        spectrum = await _spectrumAnalyzer.AnalyzeAsync(
                            audioFiles[0],
                            new SpectrumAnalysisOptions(),
                            linkedToken) ?? SpectrumAnalysis.Empty;
                    }

                    linkedToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Spectrum analysis failed for batch item {InputPath}", current.InputPath);
                    spectrum = SpectrumAnalysis.Empty;
                }

                if (spectrum.BandEnergyDb.Count == 0)
                {
                    Log.Warning(
                        "Spectrum analysis returned no data for {InputPath}; NyquistCutoffHz and BrickwallScore are defaults",
                        current.InputPath);
                }
                else
                {
                    Log.Information(
                        "Spectrum analysis for {InputPath}: NyquistCutoffHz={NyquistCutoffHz:F0} BrickwallScore={BrickwallScore:F3} BrickwallSuspected={BrickwallSuspected} Bands={BandCount}",
                        current.InputPath,
                        spectrum.NyquistCutoffHz,
                        spectrum.BrickwallScore,
                        spectrum.IsBrickwallSuspected,
                        spectrum.BandEnergyDb.Count);
                }

                current = current with { State = BatchItemState.ReadingOriginSignals };
                items[i] = current;
                ReportProgress(progress, total, completed, failed, cancelled, items, current, false);

                OriginSignals origin = OriginSignals.Empty;
                try
                {
                    // Build minimal AlbumMetadata from scan for origin reading
                    var meta = scanResult.Album;
                    origin = await _originSignalReader.ReadAsync(meta ?? new AlbumMetadata(), linkedToken);
                    linkedToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Origin signal reading failed for batch item");
                    origin = OriginSignals.Empty;
                }

                RipLogVerification logVerification = RipLogVerification.Empty;
                if (_ripLogVerifier is not null)
                {
                    current = current with { State = BatchItemState.VerifyingLog };
                    items[i] = current;
                    ReportProgress(progress, total, completed, failed, cancelled, items, current, false);

                    try
                    {
                        var albumFolder = scanResult.AlbumFolder ??
                                          System.IO.Path.GetDirectoryName(cueFixResult.Cue.FilePath);
                        logVerification = await _ripLogVerifier.VerifyAsync(
                            scanResult.Album ?? new AlbumMetadata(),
                            albumFolder,
                            scanResult.AudioFilePaths,
                            linkedToken);
                        linkedToken.ThrowIfCancellationRequested();
                    }
                    catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Log verification failed for batch item {InputPath}", current.InputPath);
                        logVerification = RipLogVerification.Empty;
                    }
                }

                current = current with { State = BatchItemState.Classifying };
                items[i] = current;
                ReportProgress(progress, total, completed, failed, cancelled, items, current, false);

                var resultWithLog = partialResult with { Log = logVerification };
                var finalResult = _classifier.Classify(resultWithLog, spectrum, origin);
                current = current with { Result = finalResult, State = BatchItemState.Completed, CompletedAt = DateTimeOffset.UtcNow };
                items[i] = current;
                completed++;
                ReportProgress(progress, total, completed, failed, cancelled, items, current, false);

                try
                {
                    if (scanResult.Album is not null)
                    {
                        var report = _reportBuilder.Build(
                            scanResult.Album,
                            cueFixResult.Cue,
                            finalResult,
                            cueFixResult.Warnings);
                        var reportFolder = System.IO.Path.GetDirectoryName(cueFixResult.Cue.FilePath) ?? string.Empty;
                        await _reportWriter.WriteJsonAsync(reportFolder, report, linkedToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to write report for batch item");
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                current = current with { State = BatchItemState.Cancelled, CompletedAt = DateTimeOffset.UtcNow };
                items[i] = current;
                cancelled++;
                for (int j = i + 1; j < items.Count; j++)
                {
                    items[j] = items[j] with { State = BatchItemState.Cancelled, CompletedAt = DateTimeOffset.UtcNow };
                    cancelled++;
                }
                ReportProgress(progress, total, completed, failed, cancelled, items, current, true);
                break;
            }
            catch (Exception) when (linkedToken.IsCancellationRequested)
            {
                current = current with { State = BatchItemState.Cancelled, CompletedAt = DateTimeOffset.UtcNow };
                items[i] = current;
                cancelled++;
                ReportProgress(progress, total, completed, failed, cancelled, items, current, false);
                continue;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Batch item failed for {InputPath}", current.InputPath);
                current = current with
                {
                    State = BatchItemState.Failed,
                    Errors = new[] { $"{ex.GetType().Name}: {ex.Message}" },
                    CompletedAt = DateTimeOffset.UtcNow
                };
                items[i] = current;
                failed++;
                ReportProgress(progress, total, completed, failed, cancelled, items, current, false);
            }
        }

        ReportProgress(progress, total, completed, failed, cancelled, items, null, true);
    }

    private static AlbumMetadata BuildSpectrumAlbum(ScanResult scan, IReadOnlyList<string> audioFiles)
    {
        if (scan.Album is not null && scan.Album.Tracks.Any(track => !string.IsNullOrWhiteSpace(track.FilePath)))
        {
            return scan.Album;
        }

        var tracks = audioFiles
            .Select((path, index) => new TrackMetadata
            {
                Number = index + 1,
                Title = System.IO.Path.GetFileNameWithoutExtension(path),
                FilePath = path
            })
            .ToList();

        return new AlbumMetadata
        {
            Album = scan.Album?.Album ?? string.Empty,
            Artist = scan.Album?.Artist ?? string.Empty,
            Tracks = tracks,
            DbPoweramp = scan.Album?.DbPoweramp ?? DbPowerampEvidence.Empty
        };
    }

    private static string? FirstMeaningfulLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed.Length > 400 ? trimmed[..400] : trimmed;
            }
        }

        return null;
    }

    private static void ReportProgress(IProgress<BatchProgress> progress, int total, int completed, int failed, int cancelled, List<BatchItem> items, BatchItem? current, bool finished)
    {
        progress.Report(new BatchProgress
        {
            Total = total,
            Completed = completed,
            Failed = failed,
            Cancelled = cancelled,
            CurrentItem = current,
            Items = items.ToList(),
            IsFinished = finished
        });
    }
}
