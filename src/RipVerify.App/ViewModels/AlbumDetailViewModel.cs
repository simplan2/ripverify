using System.Globalization;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using RipVerify.Infrastructure.Cue;
using RipVerify.Infrastructure.Reporting;
using Serilog;

namespace RipVerify.App.ViewModels;

public partial class AlbumDetailViewModel : ViewModelBase
{
    private static readonly string[] SpectrumAudioExtensions = { ".flac", ".wav", ".ape", ".wv" };
    private static readonly IBrush TrackOkBrush = new SolidColorBrush(Color.Parse("#A6E3A1"));
    private static readonly IBrush TrackWarningBrush = new SolidColorBrush(Color.Parse("#F9E2AF"));
    private static readonly IBrush TrackDangerBrush = new SolidColorBrush(Color.Parse("#FF5C4D"));
    private static readonly IBrush TrackUnknownBrush = new SolidColorBrush(Color.Parse("#8A8F98"));

    private readonly ICueToolsRunner _cueToolsRunner;
    private readonly CueToolsOutputParser _cueToolsParser;
    private readonly ISpectrumAnalyzer _spectrumAnalyzer;
    private readonly IOriginSignalReader _originSignalReader;
    private readonly IVerificationClassifier _classifier;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private readonly IReportDocumentBuilder _reportBuilder;
    private readonly IReportWriter _reportWriter;
    private readonly IRipLogVerifier? _ripLogVerifier;
    private readonly object _verificationSync = new();
    private CancellationTokenSource? _verificationCts;
    private Task? _verificationTask;
    private const double SpectrumLowCutoffHz = 20000.0;

    [ObservableProperty]
    private VerificationResult? _result;

    [ObservableProperty]
    private AlbumMetadata? _album;

    [ObservableProperty]
    private CueSheet? _cueSheet;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = string.Empty;

    [ObservableProperty]
    private string _cueToolsCardTitle = string.Empty;

    [ObservableProperty]
    private string _spectrumCardTitle = string.Empty;

    [ObservableProperty]
    private string _originCardTitle = string.Empty;

    [ObservableProperty]
    private string _tracksCardTitle = string.Empty;

    [ObservableProperty]
    private string _diagnosticsTitle = string.Empty;

    [ObservableProperty]
    private string _verifyAgainText = string.Empty;

    [ObservableProperty]
    private string _closeText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _statusDescriptionText = string.Empty;

    [ObservableProperty]
    private string _statusIconText = string.Empty;

    [ObservableProperty]
    private string _accurateRipText = string.Empty;

    [ObservableProperty]
    private string _ctdbStatusText = string.Empty;

    [ObservableProperty]
    private string _confidenceText = string.Empty;

    [ObservableProperty]
    private string _driveOffsetText = string.Empty;

    [ObservableProperty]
    private string _ctdbParityText = string.Empty;

    [ObservableProperty]
    private string _cueOriginText = string.Empty;

    [ObservableProperty]
    private bool _hasCueOriginWarning;

    [ObservableProperty]
    private string _cueOriginWarningText = string.Empty;

    [ObservableProperty]
    private bool _hasDbPowerampVerified;

    [ObservableProperty]
    private string _dbPowerampText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _parserWarnings = Array.Empty<string>();

    [ObservableProperty]
    private bool _hasParserWarnings;

    [ObservableProperty]
    private IReadOnlyList<string> _criticalParserWarnings = Array.Empty<string>();

    [ObservableProperty]
    private bool _hasCriticalParserWarnings;

    [ObservableProperty]
    private string _parserWarningsTitle = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _diagnostics = Array.Empty<string>();

    [ObservableProperty]
    private bool _hasDiagnostics;

    [ObservableProperty]
    private string _nyquistCutoffText = string.Empty;

    [ObservableProperty]
    private string _spectrumVerdictText = string.Empty;

    [ObservableProperty]
    private string _fakeLosslessLabelText = string.Empty;

    [ObservableProperty]
    private bool _isSpectrumAvailable;

    [ObservableProperty]
    private bool _isSpectrumClean;

    [ObservableProperty]
    private bool _isSpectrumSuspected;

    [ObservableProperty]
    private bool _isMasteringFilterDetected;

    [ObservableProperty]
    private string _masteringFilterText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<BandBarViewModel> _bandBars = Array.Empty<BandBarViewModel>();

    [ObservableProperty]
    private string _spectrumChartTitle = string.Empty;

    [ObservableProperty]
    private string _spectrumStartLabel = string.Empty;

    [ObservableProperty]
    private string _spectrumEndLabel = string.Empty;

    [ObservableProperty]
    private Geometry? _spectrumAreaData;

    [ObservableProperty]
    private Geometry? _spectrumLineData;

    [ObservableProperty]
    private IBrush _spectrumChartBrush = new SolidColorBrush(Color.Parse("#A6E3A1"));

    [ObservableProperty]
    private string _albumPathText = string.Empty;

    [ObservableProperty]
    private string _cuePathText = string.Empty;

    [ObservableProperty]
    private string _worstTrackPathText = string.Empty;

    [ObservableProperty]
    private bool _hasAlbumPath;

    [ObservableProperty]
    private bool _hasCuePath;

    [ObservableProperty]
    private string _originSummaryText = string.Empty;

    [ObservableProperty]
    private bool _hasPlatformIsrc;

    [ObservableProperty]
    private string _platformIsrcText = string.Empty;

    [ObservableProperty]
    private string _distributorText = string.Empty;

    [ObservableProperty]
    private bool _hasDistributor;

    [ObservableProperty]
    private IReadOnlyList<TrackRowViewModel> _trackRows = Array.Empty<TrackRowViewModel>();

    [ObservableProperty]
    private bool _hasTracks;

    [ObservableProperty]
    private string _trackNumberColumnText = string.Empty;

    [ObservableProperty]
    private string _trackTitleColumnText = string.Empty;

    [ObservableProperty]
    private string _trackDurationColumnText = string.Empty;

    [ObservableProperty]
    private string _trackSpectrumColumnText = string.Empty;

    [ObservableProperty]
    private string _trackStatusColumnText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _cueWarnings = Array.Empty<string>();

    [ObservableProperty]
    private bool _hasCueWarnings;

    [ObservableProperty]
    private bool _hasLog;

    [ObservableProperty]
    private string _logStatusText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<string> _logWarnings = Array.Empty<string>();

    [ObservableProperty]
    private bool _hasLogWarnings;

    [ObservableProperty]
    private string _originEvidenceText = string.Empty;

    [ObservableProperty]
    private string _originMetadataNote = string.Empty;

    [ObservableProperty]
    private bool _hasMetadataWarning;

    [ObservableProperty]
    private string _metadataWarningText = string.Empty;

    [ObservableProperty]
    private string _worstTrackText = string.Empty;

    [ObservableProperty]
    private string _worstTrackFullPath = string.Empty;

    [ObservableProperty]
    private bool _hasWorstTrack;

    [ObservableProperty]
    private bool _forceEnglish;

    [ObservableProperty]
    private string _exportMarkdownText = string.Empty;

    [ObservableProperty]
    private string _forceEnglishText = string.Empty;

    [ObservableProperty]
    private string _exportMarkdownToolTip = string.Empty;

    [ObservableProperty]
    private string _forceEnglishToolTip = string.Empty;

    [ObservableProperty]
    private string _verifyAgainToolTip = string.Empty;

    [ObservableProperty]
    private string _closeToolTip = string.Empty;

    [ObservableProperty]
    private string _notificationText = string.Empty;

    [ObservableProperty]
    private bool _isNotificationVisible;

    public event EventHandler? CloseRequested;

    public AlbumDetailViewModel(
        ICueToolsRunner cueToolsRunner,
        CueToolsOutputParser cueToolsParser,
        ISpectrumAnalyzer spectrumAnalyzer,
        IOriginSignalReader originSignalReader,
        IVerificationClassifier classifier,
        ISettingsService settingsService,
        ILocalizationService localizationService,
        IReportDocumentBuilder reportBuilder,
        IReportWriter reportWriter,
        IRipLogVerifier? ripLogVerifier = null)
    {
        _cueToolsRunner = cueToolsRunner;
        _cueToolsParser = cueToolsParser;
        _spectrumAnalyzer = spectrumAnalyzer;
        _originSignalReader = originSignalReader;
        _classifier = classifier;
        _settingsService = settingsService;
        _localizationService = localizationService;
        _reportBuilder = reportBuilder;
        _reportWriter = reportWriter;
        _ripLogVerifier = ripLogVerifier;

        UpdateStrings();
        UpdateResultTexts();
        _localizationService.LanguageChanged += (_, _) =>
        {
            UpdateStrings();
            UpdateResultTexts();
        };
    }

    public void Load(
        VerificationResult result,
        AlbumMetadata album,
        CueSheet cueSheet,
        IReadOnlyList<string>? cueWarnings = null)
    {
        Album = album;
        CueSheet = cueSheet;
        CueWarnings = cueWarnings ?? Array.Empty<string>();
        var enrichOrigin = result.CueOrigin == CueOrigin.Original && cueSheet.Origin != CueOrigin.Original;
        var enrichDbPoweramp = !HasDbPowerampEvidence(result.DbPoweramp) && HasDbPowerampEvidence(album.DbPoweramp);
        Result = enrichOrigin || enrichDbPoweramp
            ? result with
            {
                CueOrigin = enrichOrigin ? cueSheet.Origin : result.CueOrigin,
                DbPoweramp = enrichDbPoweramp ? album.DbPoweramp : result.DbPoweramp
            }
            : result;
    }

    private static bool HasDbPowerampEvidence(DbPowerampEvidence evidence)
    {
        return evidence.IsAccurate ||
               !string.IsNullOrWhiteSpace(evidence.AccurateRipDiscId) ||
               !string.IsNullOrWhiteSpace(evidence.AccurateRipResult) ||
               evidence.Tracks.Count > 0;
    }

    partial void OnResultChanged(VerificationResult? value) => UpdateResultTexts();

    partial void OnAlbumChanged(AlbumMetadata? value) => UpdateResultTexts();

    partial void OnCueWarningsChanged(IReadOnlyList<string> value) =>
        HasCueWarnings = value.Count > 0;

    private void UpdateStrings()
    {
        CueToolsCardTitle = _localizationService.GetString("AlbumDetail_CueToolsCard");
        SpectrumCardTitle = _localizationService.GetString("AlbumDetail_SpectrumCard");
        OriginCardTitle = _localizationService.GetString("AlbumDetail_OriginCard");
        TracksCardTitle = _localizationService.GetString("AlbumDetail_TracksCard");
        DiagnosticsTitle = _localizationService.GetString("AlbumDetail_Diagnostics");
        FakeLosslessLabelText = _localizationService.GetString("AlbumDetail_FakeLossless");
        MasteringFilterText = _localizationService.GetString("AlbumDetail_MasteringFilter");
        VerifyAgainText = _localizationService.GetString("AlbumDetail_VerifyAgain");
        CloseText = _localizationService.GetString("AlbumDetail_Close");
        ExportMarkdownText = _localizationService.GetString("Report_ExportMarkdown");
        ForceEnglishText = _localizationService.GetString("Report_ForceEnglish");
        ExportMarkdownToolTip = _localizationService.GetString("Tooltip_ExportMarkdown");
        ForceEnglishToolTip = _localizationService.GetString("Tooltip_ForceEnglish");
        VerifyAgainToolTip = _localizationService.GetString("Tooltip_Verify");
        CloseToolTip = _localizationService.GetString("Tooltip_CloseDetail");
        TrackNumberColumnText = _localizationService.GetString("AlbumDetail_ColumnNumber");
        TrackTitleColumnText = _localizationService.GetString("AlbumDetail_ColumnTitle");
        TrackDurationColumnText = _localizationService.GetString("AlbumDetail_ColumnDuration");
        TrackSpectrumColumnText = _localizationService.GetString("AlbumDetail_ColumnSpectrum");
        TrackStatusColumnText = _localizationService.GetString("AlbumDetail_ColumnStatus");
        SpectrumStartLabel = _localizationService.GetString("Spectrum_AxisStart");
        SpectrumEndLabel = _localizationService.GetString("Spectrum_AxisEnd");
        SpectrumChartTitle = _localizationService.GetString("AlbumDetail_SpectrumCard");
        OriginMetadataNote = _localizationService.GetString("AlbumDetail_OriginMetadataNote");
        ParserWarningsTitle = _localizationService.GetString("AlbumDetail_ParserWarnings");
    }

    private void UpdateResultTexts()
    {
        var spectrum = Result?.Spectrum ?? SpectrumAnalysis.Empty;
        var origin = Result?.Origin ?? OriginSignals.Empty;
        var trackCount = Album?.Tracks.Count ?? 0;

        StatusText = Result is null ? string.Empty : _localizationService.GetString($"Status_{Result.Status}");
        StatusDescriptionText = Result is null
            ? string.Empty
            : _localizationService.GetString($"Status_{Result.Status}_Description");
        StatusIconText = Result?.Status switch
        {
            VerificationStatus.VerifiedCdRip => "✓",
            VerificationStatus.VerifiedWithWarnings => "⚠",
            VerificationStatus.AuthenticRipWithLog => "▤",
            VerificationStatus.FakeLossless => "✕",
            VerificationStatus.Unprocessable => "!",
            VerificationStatus.LikelyStreaming => "♪",
            _ => "?"
        };
        AccurateRipText = Result is null ? string.Empty : FormatAccurateRip(Result.AccurateRipStatus);
        CtdbStatusText = Result is null ? string.Empty : FormatCtdb(Result.CtdbStatus, trackCount);
        ConfidenceText = string.Format(
            CultureInfo.CurrentCulture,
            _localizationService.GetString("AlbumDetail_Confidence"),
            Result?.ConfidenceScore ?? 0);
        DriveOffsetText = string.Format(
            CultureInfo.CurrentCulture,
            _localizationService.GetString("AlbumDetail_DriveOffset"),
            Result?.DriveOffset ?? 0);
        CtdbParityText = string.Format(
            CultureInfo.CurrentCulture,
            _localizationService.GetString("AlbumDetail_CtdbParity"),
            _localizationService.GetString(Result?.CtdbParityAvailable == true
                ? "AlbumDetail_CtdbParityAvailable"
                : "AlbumDetail_CtdbParityUnavailable"));

        var cueOrigin = Result?.CueOrigin ?? CueOrigin.Original;
        if (cueOrigin == CueOrigin.Original && CueSheet is not null)
        {
            cueOrigin = CueSheet.Origin;
        }
        CueOriginText = Result is null
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("AlbumDetail_CueOrigin"),
                _localizationService.GetString($"CueOrigin_{cueOrigin}"));
        HasCueOriginWarning = Result is not null && cueOrigin == CueOrigin.GeneratedFromMetadata;
        CueOriginWarningText = HasCueOriginWarning
            ? _localizationService.GetString("AlbumDetail_CueOriginWarning")
            : string.Empty;

        HasDbPowerampVerified = Result?.DbPoweramp?.IsAccurate == true;
        DbPowerampText = HasDbPowerampVerified
            ? _localizationService.GetString("AlbumDetail_DbPowerampVerified")
            : string.Empty;

        ParserWarnings = Result?.ParserWarnings ?? Array.Empty<string>();
        HasParserWarnings = ParserWarnings.Count > 0;
        CriticalParserWarnings = ParserWarnings
            .Where(IsCriticalParserWarning)
            .ToList();
        HasCriticalParserWarnings = CriticalParserWarnings.Count > 0;
        Diagnostics = (Result?.Diagnostics ?? Array.Empty<string>())
            .Select(LocalizeDiagnostic)
            .ToList();
        HasDiagnostics = Diagnostics.Count > 0;

        IsSpectrumAvailable = Result is not null && spectrum.BandEnergyDb.Count > 0 && spectrum.NyquistCutoffHz > 0;
        if (IsSpectrumAvailable)
        {
            NyquistCutoffText = string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("AlbumDetail_NyquistCutoff"),
                spectrum.NyquistCutoffHz / 1000.0);
            IsSpectrumSuspected = spectrum.IsBrickwallSuspected;
            IsMasteringFilterDetected = spectrum.MasteringFilterDetected;
            IsSpectrumClean = !spectrum.IsBrickwallSuspected && !spectrum.MasteringFilterDetected;
            SpectrumVerdictText = _localizationService.GetString(IsSpectrumSuspected
                ? "AlbumDetail_PossibleLossy"
                : "AlbumDetail_NotDetected");
            BandBars = BuildBandBars(spectrum.BandEnergyDb);
            (SpectrumAreaData, SpectrumLineData) = BuildSpectrumGeometry(
                spectrum.BandEnergyDb,
                spectrum.BandEdgesHz);
            SpectrumChartBrush = new SolidColorBrush(Color.Parse(
                IsSpectrumSuspected ? "#FF5C4D" : "#A6E3A1"));
        }
        else
        {
            NyquistCutoffText = Result is null ? string.Empty : _localizationService.GetString("AlbumDetail_SpectrumUnavailable");
            IsSpectrumSuspected = false;
            IsMasteringFilterDetected = false;
            IsSpectrumClean = false;
            SpectrumVerdictText = string.Empty;
            BandBars = Array.Empty<BandBarViewModel>();
            SpectrumAreaData = null;
            SpectrumLineData = null;
        }

        var evidenceKey = origin.StreamingConfidence >= 0.8
            ? "AlbumDetail_OriginEvidenceHigh"
            : origin.StreamingConfidence >= 0.5
                ? "AlbumDetail_OriginEvidenceMedium"
                : "AlbumDetail_OriginEvidenceLow";
        OriginEvidenceText = Result is null ? string.Empty : _localizationService.GetString(evidenceKey);
        OriginSummaryText = OriginEvidenceText;
        HasPlatformIsrc = Result is not null &&
                          origin.HasPlatformIsrc &&
                          !string.IsNullOrWhiteSpace(origin.PlatformIsrcExample);
        PlatformIsrcText = HasPlatformIsrc
            ? string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("AlbumDetail_OriginPlatformIsrc"),
                origin.PlatformIsrcExample)
            : string.Empty;
        HasDistributor = Result is not null && !string.IsNullOrWhiteSpace(origin.DigitalDistributor);
        DistributorText = HasDistributor
            ? string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("AlbumDetail_Distributor"),
                origin.DigitalDistributor)
            : string.Empty;

        var albumPath = Result is null ? string.Empty : ResolveAlbumFolder(Result, CueSheet);
        HasAlbumPath = !string.IsNullOrWhiteSpace(albumPath);
        AlbumPathText = HasAlbumPath
            ? string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("AlbumDetail_AlbumPath"),
                albumPath)
            : string.Empty;
        var cuePath = Result?.CuePath ?? CueSheet?.FilePath;
        HasCuePath = !string.IsNullOrWhiteSpace(cuePath);
        CuePathText = HasCuePath
            ? string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("AlbumDetail_CuePath"),
                cuePath)
            : string.Empty;

        var worstTrack = Result?.Spectrum.WorstTrackFilePath;
        HasWorstTrack = !string.IsNullOrWhiteSpace(worstTrack);
        WorstTrackFullPath = worstTrack ?? string.Empty;
        WorstTrackText = HasWorstTrack
            ? string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("Spectrum_WorstTrack"),
                Path.GetFileName(worstTrack ?? string.Empty))
            : string.Empty;
        WorstTrackPathText = HasWorstTrack
            ? string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("AlbumDetail_WorstTrackPath"),
                worstTrack)
            : string.Empty;

        HasCueWarnings = CueWarnings.Count > 0;

        var log = Result?.Log ?? RipLogVerification.Empty;
        HasLog = log.HasLog;
        LogWarnings = log.Warnings;
        HasLogWarnings = LogWarnings.Count > 0;
        LogStatusText = BuildLogStatusText(log);

        var trackAnalysisByPath = (Result?.Spectrum.Tracks ?? Array.Empty<SpectrumTrackAnalysis>())
            .Where(item => !string.IsNullOrWhiteSpace(item.FilePath))
            .GroupBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var tracks = Album?.Tracks ?? Array.Empty<TrackMetadata>();
        TrackRows = tracks
            .Select(track => BuildTrackRow(track, trackAnalysisByPath))
            .ToList();
        HasTracks = TrackRows.Count > 0;

        var metadataMissing = Album is null ||
                              Album.Tracks.Count == 0 ||
                              string.IsNullOrWhiteSpace(Album.Album);
        var metadataFallback = Album?.MetadataFallbackUsed == true;
        HasMetadataWarning = metadataMissing || metadataFallback;
        MetadataWarningText = metadataMissing
            ? _localizationService.GetString("AlbumDetail_MetadataMissing")
            : metadataFallback
                ? _localizationService.GetString("AlbumDetail_MetadataFallback")
                : string.Empty;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "--:--";
        }

        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }

    private static BandBarViewModel[] BuildBandBars(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return Array.Empty<BandBarViewModel>();
        }

        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        if (range < 0.001)
        {
            range = 1;
        }

        return values
            .Select(value => new BandBarViewModel(8 + ((value - min) / range * 48), value))
            .ToArray();
    }

    private static (Geometry? Area, Geometry? Line) BuildSpectrumGeometry(
        IReadOnlyList<double> values,
        IReadOnlyList<double> edges)
    {
        if (values.Count == 0)
        {
            return (null, null);
        }

        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        if (range < 0.001)
        {
            range = 1;
        }

        var points = new List<Point>();
        if (edges.Count >= values.Count && edges.Count > 0)
        {
            for (var index = 0; index < values.Count; index++)
            {
                var frequency = Math.Clamp(edges[index], 20.0, 22050.0);
                var x = Math.Log10(frequency / 20.0) / Math.Log10(22050.0 / 20.0) * 100.0;
                var y = 4.0 + (1.0 - ((values[index] - min) / range)) * 92.0;
                if (index == 0)
                {
                    points.Add(new Point(0.0, y));
                }

                points.Add(new Point(x, y));
            }
        }
        else
        {
            for (var index = 0; index < values.Count; index++)
            {
                var x = values.Count == 1 ? 0.0 : index / (double)(values.Count - 1) * 100.0;
                var y = 4.0 + (1.0 - ((values[index] - min) / range)) * 92.0;
                points.Add(new Point(x, y));
            }
        }

        if (points.Count == 1)
        {
            points.Add(new Point(100.0, points[0].Y));
        }

        if (points[^1].X < 100.0)
        {
            points.Add(new Point(100.0, points[^1].Y));
        }

        var lineFigure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = false
        };
        for (var index = 1; index < points.Count; index++)
        {
            lineFigure.Segments!.Add(new LineSegment { Point = points[index] });
        }

        var line = new PathGeometry();
        line.Figures!.Add(lineFigure);

        var areaFigure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true
        };
        for (var index = 1; index < points.Count; index++)
        {
            areaFigure.Segments!.Add(new LineSegment { Point = points[index] });
        }

        areaFigure.Segments!.Add(new LineSegment { Point = new Point(100.0, 100.0) });
        areaFigure.Segments.Add(new LineSegment { Point = new Point(0.0, 100.0) });

        var area = new PathGeometry();
        area.Figures!.Add(areaFigure);

        return (area, line);
    }

    private TrackRowViewModel BuildTrackRow(
        TrackMetadata track,
        Dictionary<string, SpectrumTrackAnalysis> trackAnalysisByPath)
    {
        var incomplete = track.IsIncomplete ||
                         track.Warnings.Contains("TrackIncomplete", StringComparer.Ordinal);
        trackAnalysisByPath.TryGetValue(track.FilePath ?? string.Empty, out var analysis);

        var spectrumLow = analysis is not null &&
                          !analysis.MasteringFilterDetected &&
                          (analysis.IsBrickwallSuspected ||
                           analysis.NyquistCutoffHz > 0 && analysis.NyquistCutoffHz < SpectrumLowCutoffHz);

        string statusKey;
        IBrush statusBrush;
        if (incomplete)
        {
            statusKey = "Track_StatusIncomplete";
            statusBrush = TrackWarningBrush;
        }
        else if (spectrumLow)
        {
            statusKey = "Track_StatusSpectrumLow";
            statusBrush = TrackDangerBrush;
        }
        else if (analysis is not null)
        {
            statusKey = "Track_StatusOk";
            statusBrush = TrackOkBrush;
        }
        else
        {
            statusKey = "Track_StatusUnknown";
            statusBrush = TrackUnknownBrush;
        }

        var status = _localizationService.GetString(statusKey);
        var warning = incomplete
            ? _localizationService.GetString("AlbumDetail_TrackIncomplete")
            : string.Empty;
        return new TrackRowViewModel(
            track.Number,
            track.Number.ToString("00", CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(track.Title)
                ? Path.GetFileName(track.FilePath) ?? string.Empty
                : track.Title,
            FormatDuration(track.Duration),
            incomplete,
            warning,
            analysis is not null && analysis.NyquistCutoffHz > 0
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    _localizationService.GetString("AlbumDetail_TrackSpectrum"),
                    analysis.NyquistCutoffHz / 1000.0)
                : "--",
            status,
            statusBrush);
    }

    private string BuildLogStatusText(RipLogVerification log)
    {
        if (!log.HasLog)
        {
            return string.Empty;
        }

        string status;
        if (log.ComparedTracks == 0)
        {
            status = _localizationService.GetString("AlbumDetail_LogNoCrc");
        }
        else if (log.MismatchedTrackNumbers.Count > 0)
        {
            status = string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("AlbumDetail_LogMismatch"),
                log.MismatchedTrackNumbers.Count);
        }
        else
        {
            status = string.Format(
                CultureInfo.CurrentCulture,
                _localizationService.GetString("AlbumDetail_LogVerified"),
                log.MatchedTracks,
                log.ComparedTracks);
        }

        if (!string.IsNullOrWhiteSpace(log.DriveName))
        {
            status = string.Concat(
                status,
                " · ",
                string.Format(
                    CultureInfo.CurrentCulture,
                    _localizationService.GetString("AlbumDetail_LogDrive"),
                    log.DriveName));
        }

        return status;
    }

    private static bool IsCriticalParserWarning(string warning)
    {
        return warning.Contains("differs", StringComparison.OrdinalIgnoreCase) ||
               warning.Contains("no match", StringComparison.OrdinalIgnoreCase) ||
               warning.Contains("mismatch", StringComparison.OrdinalIgnoreCase) ||
               warning.Contains("inaccurate", StringComparison.OrdinalIgnoreCase) ||
               warning.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               warning.Contains("error", StringComparison.OrdinalIgnoreCase);
    }

    private string LocalizeDiagnostic(string diagnostic)
    {
        return diagnostic is "Classifier_CtdbVerifiedWithArMismatch" or
            "Classifier_DbPowerampAccurateWithArMismatch"
            ? _localizationService.GetString(diagnostic)
            : diagnostic;
    }

    private string FormatAccurateRip(AccurateRipStatus status)
    {
        return status switch
        {
            AccurateRipStatus.AccuratelyRipped => _localizationService.GetString("AlbumDetail_AccurateRipVerified"),
            AccurateRipStatus.NotFound => _localizationService.GetString("AlbumDetail_AccurateRipNotFound"),
            AccurateRipStatus.Inaccurate => _localizationService.GetString("AlbumDetail_AccurateRipInaccurate"),
            _ => _localizationService.GetString("AlbumDetail_AccurateRipUnprocessable")
        };
    }

    private string FormatCtdb(CtdbStatus status, int trackCount)
    {
        return status switch
        {
            CtdbStatus.AccuratelyRipped => trackCount > 0
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    _localizationService.GetString("AlbumDetail_CtdbVerified"),
                    trackCount,
                    trackCount)
                : _localizationService.GetString("AlbumDetail_CtdbVerifiedShort"),
            CtdbStatus.NotFound => _localizationService.GetString("AlbumDetail_CtdbNotFound"),
            CtdbStatus.Inaccurate => _localizationService.GetString("AlbumDetail_CtdbInaccurate"),
            _ => _localizationService.GetString("AlbumDetail_CtdbUnprocessable")
        };
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task VerifyAgainAsync(CancellationToken cancellationToken)
    {
        var cueSheet = CueSheet;
        if (cueSheet is null || string.IsNullOrEmpty(cueSheet.FilePath))
        {
            return;
        }

        var album = Album;
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_verificationSync)
        {
            _verificationCts = linkedCts;
        }

        IsBusy = true;
        BusyText = _localizationService.GetString("AlbumDetail_Verifying");

        Task<VerificationResult>? verificationTask = null;
        try
        {
            verificationTask = Task.Run(
                () => RunVerificationAsync(album, cueSheet, linkedCts.Token),
                linkedCts.Token);
            lock (_verificationSync)
            {
                _verificationTask = verificationTask;
            }

            var result = await verificationTask;
            linkedCts.Token.ThrowIfCancellationRequested();
            Result = result;
            await WriteJsonReportAsync(result, album, cueSheet, linkedCts.Token);
        }
        finally
        {
            lock (_verificationSync)
            {
                if (ReferenceEquals(_verificationTask, verificationTask))
                {
                    _verificationTask = null;
                }

                if (ReferenceEquals(_verificationCts, linkedCts))
                {
                    _verificationCts = null;
                }
            }

            linkedCts.Dispose();
            IsBusy = false;
            BusyText = string.Empty;
        }
    }

    public void CancelRunningVerification()
    {
        CancellationTokenSource? cts;
        lock (_verificationSync)
        {
            cts = _verificationCts;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async Task StopAsync()
    {
        CancelRunningVerification();

        Task? task;
        lock (_verificationSync)
        {
            task = _verificationTask;
        }

        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            Log.Warning("Timed out waiting for album verification to stop during shutdown");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed while stopping album verification during shutdown");
        }
    }

    private async Task<VerificationResult> RunVerificationAsync(
        AlbumMetadata? album,
        CueSheet cueSheet,
        CancellationToken cancellationToken)
    {
        var audioDir = System.IO.Path.GetDirectoryName(cueSheet.FilePath) ?? string.Empty;
        var runResult = await _cueToolsRunner
            .RunAsync(cueSheet, audioDir, cancellationToken)
            .ConfigureAwait(false);

        if (runResult.Process.WasCancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var partialResult = CueToolsOutputParser.Parse(runResult, cueSheet, _settingsService.Current);
        var audioPaths = ResolveSpectrumAudioPaths(album, cueSheet);

        SpectrumAnalysis spectrum = SpectrumAnalysis.Empty;
        try
        {
            if (audioPaths.Count == 0)
            {
                Log.Warning("No audio file available for spectrum analysis while verifying {AlbumPath}", cueSheet.FilePath);
            }
            else if (audioPaths.Count > 1)
            {
                spectrum = await _spectrumAnalyzer.AnalyzeAlbumAsync(
                    BuildSpectrumAlbum(album, audioPaths),
                    new SpectrumAnalysisOptions(),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                spectrum = await _spectrumAnalyzer.AnalyzeAsync(
                    audioPaths[0],
                    new SpectrumAnalysisOptions(),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Spectrum analysis failed while verifying {AlbumPath}", cueSheet.FilePath);
            spectrum = SpectrumAnalysis.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (spectrum.BandEnergyDb.Count == 0)
        {
            Log.Warning("Spectrum analysis returned no data for {AlbumPath}", cueSheet.FilePath);
        }
        else
        {
            Log.Information(
                "Spectrum analysis for {AlbumPath}: NyquistCutoffHz={NyquistCutoffHz:F0} BrickwallScore={BrickwallScore:F3} RawBrickwallScore={RawBrickwallScore:F3} BrickwallSuspected={BrickwallSuspected} MasteringFilterDetected={MasteringFilterDetected}",
                cueSheet.FilePath,
                spectrum.NyquistCutoffHz,
                spectrum.BrickwallScore,
                spectrum.RawBrickwallScore,
                spectrum.IsBrickwallSuspected,
                spectrum.MasteringFilterDetected);
        }

        OriginSignals origin = OriginSignals.Empty;
        try
        {
            origin = await _originSignalReader
                .ReadAsync(album ?? new AlbumMetadata(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            origin = OriginSignals.Empty;
        }

        RipLogVerification logVerification = RipLogVerification.Empty;
        if (_ripLogVerifier is not null)
        {
            try
            {
                logVerification = await _ripLogVerifier.VerifyAsync(
                    album ?? BuildSpectrumAlbum(album, audioPaths),
                    audioDir,
                    audioPaths,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Log verification failed while verifying {AlbumPath}", cueSheet.FilePath);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return _classifier.Classify(
            partialResult with
            {
                Log = logVerification,
                CueOrigin = cueSheet.Origin,
                DbPoweramp = album?.DbPoweramp ?? DbPowerampEvidence.Empty
            },
            spectrum,
            origin);
    }

    private static IReadOnlyList<string> ResolveSpectrumAudioPaths(AlbumMetadata? album, CueSheet cueSheet)
    {
        if (album is not null)
        {
            var albumPaths = album.Tracks
                .Select(track => track.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (albumPaths.Count > 0)
            {
                return albumPaths;
            }
        }

        if (string.IsNullOrWhiteSpace(cueSheet.FilePath))
        {
            return Array.Empty<string>();
        }

        var cueDirectory = Path.GetDirectoryName(cueSheet.FilePath) ?? string.Empty;
        var cuePaths = new List<string>();
        foreach (var track in cueSheet.Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.FileName))
            {
                continue;
            }

            var candidate = Path.IsPathRooted(track.FileName)
                ? track.FileName
                : Path.Combine(cueDirectory, track.FileName);
            if (File.Exists(candidate) && !cuePaths.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                cuePaths.Add(candidate);
            }
        }

        if (cuePaths.Count > 0)
        {
            return cuePaths;
        }

        if (!Directory.Exists(cueDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(cueDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => SpectrumAudioExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AlbumMetadata BuildSpectrumAlbum(AlbumMetadata? album, IReadOnlyList<string> audioPaths)
    {
        if (album is not null && album.Tracks.Any(track => !string.IsNullOrWhiteSpace(track.FilePath)))
        {
            return album;
        }

        var tracks = audioPaths
            .Select((path, index) => new TrackMetadata
            {
                Number = index + 1,
                Title = Path.GetFileNameWithoutExtension(path),
                FilePath = path
            })
            .ToList();

        return new AlbumMetadata
        {
            Album = album?.Album ?? string.Empty,
            Artist = album?.Artist ?? string.Empty,
            Tracks = tracks,
            DbPoweramp = album?.DbPoweramp ?? DbPowerampEvidence.Empty
        };
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task ExportMarkdownAsync(CancellationToken cancellationToken)
    {
        if (Album is null || CueSheet is null || Result is null)
        {
            return;
        }

        var folder = ResolveAlbumFolder(Result, CueSheet);
        var document = _reportBuilder.Build(Album, CueSheet, Result, CueWarnings);
        var path = Path.Combine(folder, ReportWriter.MarkdownFileName);

        await _reportWriter.WriteMarkdownAsync(folder, document, ForceEnglish, cancellationToken);

        ShowNotification(File.Exists(path)
            ? string.Format(CultureInfo.CurrentCulture, _localizationService.GetString("Report_MarkdownWritten"), path)
            : _localizationService.GetString("Report_WriteFailed"));
    }

    private async Task WriteJsonReportAsync(
        VerificationResult result,
        AlbumMetadata? album,
        CueSheet cueSheet,
        CancellationToken cancellationToken)
    {
        if (album is null)
        {
            return;
        }

        var folder = ResolveAlbumFolder(result, cueSheet);
        var document = _reportBuilder.Build(album, cueSheet, result, CueWarnings);
        var path = Path.Combine(folder, ReportWriter.JsonFileName);

        await _reportWriter.WriteJsonAsync(folder, document, cancellationToken);

        ShowNotification(File.Exists(path)
            ? string.Format(CultureInfo.CurrentCulture, _localizationService.GetString("Report_JsonWritten"), path)
            : _localizationService.GetString("Report_WriteFailed"));
    }

    private static string ResolveAlbumFolder(VerificationResult result, CueSheet? cueSheet)
    {
        if (!string.IsNullOrWhiteSpace(result.AlbumPath) && Directory.Exists(result.AlbumPath))
        {
            return result.AlbumPath;
        }

        if (cueSheet is not null && !string.IsNullOrWhiteSpace(cueSheet.FilePath))
        {
            return Path.GetDirectoryName(cueSheet.FilePath) ?? string.Empty;
        }

        return result.AlbumPath;
    }

    private void ShowNotification(string message)
    {
        NotificationText = message;
        IsNotificationVisible = !string.IsNullOrEmpty(message);
    }

    [RelayCommand]
    private void Close()
    {
        CancelRunningVerification();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

//public sealed record BandBarViewModel(double Height, double ValueDb);

public sealed record TrackRowViewModel(
    int Number,
    string NumberText,
    string Title,
    string DurationText,
    bool IsIncomplete,
    string WarningText,
    string SpectrumText = "",
    string StatusText = "",
    IBrush? StatusBrush = null);

