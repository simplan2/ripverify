namespace RipVerify.Tests.Core;

using FluentAssertions;
using RipVerify.App.Services;

public class LocalizationServiceTests
{
    private static readonly string[] UsedKeys =
    {
        "AppTitle",
        "Batch_Cancel",
        "Batch_Details",
        "Batch_Finished",
        "Batch_Start",
        "Batch_State_Pending",
        "Batch_State_Scanning",
        "Batch_State_FixingCue",
        "Batch_State_VerifyingCueTools",
        "Batch_State_AnalyzingSpectrum",
        "Batch_State_ReadingOriginSignals",
        "Batch_State_VerifyingLog",
        "Batch_State_Classifying",
        "Batch_State_Completed",
        "Batch_State_Failed",
        "Batch_State_Cancelled",
        "Batch_State_Cancelling",
        "Batch_Running",
        "Batch_Summary",
        "CommonEnglish",
        "CommonSpanish",
        "FirstRunAcceptTerms",
        "FirstRunBack",
        "FirstRunContinueDegraded",
        "FirstRunCueToolsNotFound",
        "FirstRunDependencies",
        "FirstRunDownloadCueTools",
        "FirstRunFfmpegInfo",
        "FirstRunFinish",
        "FirstRunLanguage",
        "FirstRunNext",
        "FirstRunTitle",
        "FirstRunWelcome",
        "MainDegradedMode",
        "MainDragDropHint",
        "MainSubtitle",
        "MainDropZoneTitle",
        "MainDropZoneSubtitle",
        "MainQueueTitle",
        "MainQueueEmpty",
        "MainItemsReceived",
        "MainOpenSettings",
        "SettingsCancel",
        "SettingsCueToolsPath",
        "SettingsDriveOffset",
        "SettingsFfmpegPath",
        "SettingsLanguage",
        "SettingsLogLevel",
        "SettingsMusicBrainzUserAgent",
        "SettingsInvalidUserAgent",
        "SettingsSave",
        "SettingsTitle",
        "AlbumDetail_CueToolsCard",
        "AlbumDetail_AccurateRipVerified",
        "AlbumDetail_AccurateRipNotFound",
        "AlbumDetail_AccurateRipInaccurate",
        "AlbumDetail_AccurateRipUnprocessable",
        "AlbumDetail_CtdbVerified",
        "AlbumDetail_CtdbVerifiedShort",
        "AlbumDetail_CtdbNotFound",
        "AlbumDetail_CtdbInaccurate",
        "AlbumDetail_CtdbUnprocessable",
        "AlbumDetail_Confidence",
        "AlbumDetail_DriveOffset",
        "AlbumDetail_CtdbParity",
        "AlbumDetail_CtdbParityAvailable",
        "AlbumDetail_CtdbParityUnavailable",
        "AlbumDetail_SpectrumCard",
        "Spectrum_AxisStart",
        "Spectrum_AxisEnd",
        "AlbumDetail_NyquistCutoff",
        "AlbumDetail_SpectrumUnavailable",
        "AlbumDetail_NotDetected",
        "AlbumDetail_PossibleLossy",
        "AlbumDetail_FakeLossless",
        "AlbumDetail_AlbumPath",
        "AlbumDetail_CuePath",
        "AlbumDetail_WorstTrackPath",
        "AlbumDetail_OriginCard",
        "AlbumDetail_OriginNone",
        "AlbumDetail_OriginStreaming",
        "AlbumDetail_OriginEvidenceHigh",
        "AlbumDetail_OriginEvidenceMedium",
        "AlbumDetail_OriginEvidenceLow",
        "AlbumDetail_OriginMetadataNote",
        "AlbumDetail_OriginPlatformIsrc",
        "AlbumDetail_TracksCard",
        "AlbumDetail_TrackIncomplete",
        "AlbumDetail_ColumnNumber",
        "AlbumDetail_ColumnTitle",
        "AlbumDetail_ColumnDuration",
        "AlbumDetail_ColumnSpectrum",
        "AlbumDetail_ColumnStatus",
        "AlbumDetail_TrackSpectrum",
        "Track_StatusOk",
        "Track_StatusIncomplete",
        "Track_StatusSpectrumLow",
        "Track_StatusUnknown",
        "AlbumDetail_LogVerified",
        "AlbumDetail_LogMismatch",
        "AlbumDetail_LogNoCrc",
        "AlbumDetail_LogDrive",
        "AlbumDetail_MetadataFallback",
        "AlbumDetail_MetadataMissing",
        "AlbumDetail_Distributor",
        "AlbumDetail_CueOrigin",
        "AlbumDetail_CueOriginWarning",
        "AlbumDetail_DbPowerampVerified",
        "AlbumDetail_ParserWarnings",
        "CueOrigin_Original",
        "CueOrigin_Sanitized",
        "CueOrigin_GeneratedFromItoc",
        "CueOrigin_GeneratedFromMetadata",
        "CueOrigin_GeneratedFromTocLookup",
        "Classifier_CtdbVerifiedWithArMismatch",
        "Classifier_DbPowerampAccurateWithArMismatch",
        "AlbumDetail_Diagnostics",
        "AlbumDetail_VerifyAgain",
        "AlbumDetail_Close",
        "AlbumDetail_Verifying",
        "Status_VerifiedCdRip",
        "Status_VerifiedWithWarnings",
        "Status_AuthenticRipWithLog",
        "Status_LikelyStreaming",
        "Status_UndeterminedLossless",
        "Status_FakeLossless",
        "Status_Unprocessable",
        "Status_VerifiedCdRip_Description",
        "Status_VerifiedWithWarnings_Description",
        "Status_AuthenticRipWithLog_Description",
        "Status_LikelyStreaming_Description",
        "Status_UndeterminedLossless_Description",
        "Status_FakeLossless_Description",
        "Status_Unprocessable_Description",
        "Main_AddItems",
        "Batch_ClearAll",
        "Batch_Remove",
        "Tooltip_ClearQueue",
        "Tooltip_RemoveItem",
        "Accessibility_BatchList",
        "Report_JsonWritten",
        "Report_MarkdownWritten",
        "Report_ExportMarkdown",
        "Report_ForceEnglish",
        "Report_WriteFailed",
        "Report_Title",
        "Report_VerifiedAt",
        "Report_FinalStatus",
        "Report_CueToolsSection",
        "Report_SpectrumSection",
        "Report_OriginSection",
        "Report_LogSection",
        "Report_LogResult",
        "Report_LogDrive",
        "Report_LogPath",
        "Report_DiagnosticsSection",
        "Report_WarningsSection",
        "Report_Field",
        "Report_Value",
        "Report_AccurateRipStatus",
        "Report_CtdbStatus",
        "Report_CtdbVerified",
        "Report_CtdbVerifiedShort",
        "Report_CtdbNotFound",
        "Report_CtdbInaccurate",
        "Report_CtdbUnprocessable",
        "Report_Confidence",
        "Report_Users",
        "Report_DriveOffset",
        "Report_CtdbParity",
        "Report_CueOrigin",
        "Report_DbPoweramp",
        "Report_NyquistCutoff",
        "Report_AudioReaches",
        "Report_SpectrumVerdict",
        "Report_WorstTrack",
        "Report_OriginSummary",
        "Report_Distributor",
        "Report_None",
        "Report_Yes",
        "Report_No",
        "Report_Footer",
        "Ffmpeg_Detected",
        "Ffmpeg_NotFound",
        "Shortcut_Open",
        "Shortcut_Settings",
        "Shortcut_Verify",
        "Shortcut_Back",
        "Tooltip_OpenFolder",
        "Tooltip_OpenSettings",
        "Tooltip_StartBatch",
        "Tooltip_CancelBatch",
        "Tooltip_Details",
        "Tooltip_Verify",
        "Tooltip_Back",
        "Tooltip_SaveSettings",
        "Tooltip_CancelSettings",
        "Tooltip_ExportMarkdown",
        "Tooltip_ForceEnglish",
        "Tooltip_CloseDetail",
    };

    [Fact]
    public void SetLanguageChangesLanguage()
    {
        var service = new LocalizationService();
        service.SetLanguage("es");
        service.CurrentLanguage.Should().Be("es");
        service.SetLanguage("en");
        service.CurrentLanguage.Should().Be("en");
    }

    [Fact]
    public void ResolvesKeysInEnglish()
    {
        var service = new LocalizationService();
        service.SetLanguage("en");

        service.GetString("AppTitle").Should().Be("RipVerify");
        service.GetString("FirstRunTitle").Should().Be("Welcome to RipVerify");
        service.GetString("SettingsTitle").Should().Be("Settings");
    }

    [Fact]
    public void ResolvesKeysInSpanish()
    {
        var service = new LocalizationService();
        service.SetLanguage("es");

        service.GetString("FirstRunTitle").Should().Be("Bienvenido a RipVerify");
        service.GetString("SettingsTitle").Should().Be("Ajustes");
        service.GetString("FirstRunNext").Should().Be("Siguiente");
    }

    [Fact]
    public void ExplicitLanguageOverridesCurrentLanguage()
    {
        var service = new LocalizationService();
        service.SetLanguage("es");

        service.GetString("Report_VerifiedAt", "en").Should().Be("Verified at");
        service.GetString("Report_VerifiedAt", "es").Should().Be("Verificado el");
    }

    [Fact]
    public void AllUsedKeysResolveToDefinedValues()
    {
        var service = new LocalizationService();

        foreach (var language in new[] { "en", "es" })
        {
            service.SetLanguage(language);

            foreach (var key in UsedKeys)
            {
                service.GetString(key).Should().NotBe(key, "key '{0}' must be defined for '{1}'", key, language);
            }
        }
    }
}
