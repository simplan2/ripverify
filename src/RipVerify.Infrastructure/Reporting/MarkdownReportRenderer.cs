using System.Globalization;
using System.Text;
using RipVerify.Core.Models;
using RipVerify.Core.Services;

namespace RipVerify.Infrastructure.Reporting;

public sealed class MarkdownReportRenderer
{
    private readonly ILocalizationService _localization;

    public MarkdownReportRenderer(ILocalizationService localization)
    {
        _localization = localization;
    }

    public string Render(ReportDocument document, bool forceEnglish)
    {
        var language = forceEnglish ? "en" : _localization.CurrentLanguage;
        var entry = document.Results.Count > 0 ? document.Results[0] : null;
        var album = string.IsNullOrWhiteSpace(document.Album.Album) ? document.AlbumPath : document.Album.Album;
        var artist = string.IsNullOrWhiteSpace(document.Album.Artist) ? string.Empty : document.Album.Artist;

        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            T("Report_Title", language),
            Escape(album),
            Escape(artist)));
        builder.AppendLine();

        builder.Append("- ").Append(T("Report_VerifiedAt", language)).Append(": ")
            .AppendLine(document.VerifiedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        builder.Append("- ").Append(T("Report_FinalStatus", language)).Append(": ")
            .AppendLine(StatusLabel(entry?.Status, language));
        builder.AppendLine();

        if (entry is null)
        {
            builder.AppendLine(T("Report_None", language));
            builder.AppendLine();
            AppendFooter(builder, document, language);
            return builder.ToString();
        }

        var trackCount = document.Album.Tracks.Count;
        var accurateRip = entry.AccurateRipStatus switch
        {
            AccurateRipStatus.AccuratelyRipped => T("AlbumDetail_AccurateRipVerified", language),
            AccurateRipStatus.NotFound => T("AlbumDetail_AccurateRipNotFound", language),
            AccurateRipStatus.Inaccurate => T("AlbumDetail_AccurateRipInaccurate", language),
            _ => T("AlbumDetail_AccurateRipUnprocessable", language)
        };
        var ctdb = entry.CtdbStatus switch
        {
            CtdbStatus.AccuratelyRipped => trackCount > 0
                ? string.Format(CultureInfo.InvariantCulture, T("Report_CtdbVerified", language), trackCount, trackCount)
                : T("Report_CtdbVerifiedShort", language),
            CtdbStatus.NotFound => T("Report_CtdbNotFound", language),
            CtdbStatus.Inaccurate => T("Report_CtdbInaccurate", language),
            _ => T("Report_CtdbUnprocessable", language)
        };
        var originSummary = entry.Origin.StreamingConfidence <= 0
            ? T("AlbumDetail_OriginNone", language)
            : string.Format(CultureInfo.InvariantCulture, T("AlbumDetail_OriginStreaming", language), entry.Origin.StreamingConfidence);

        builder.Append("## ").AppendLine(T("Report_CueToolsSection", language));
        builder.AppendLine();
        builder.Append("| ").Append(T("Report_Field", language)).Append(" | ").Append(T("Report_Value", language)).AppendLine(" |");
        builder.AppendLine("| --- | --- |");
        AppendRow(builder, T("Report_AccurateRipStatus", language), Escape(accurateRip));
        AppendRow(builder, T("Report_CtdbStatus", language), Escape(ctdb));
        AppendRow(builder, T("Report_Confidence", language), string.Format(CultureInfo.InvariantCulture, T("Report_Users", language), entry.ConfidenceScore));
        AppendRow(builder, T("Report_DriveOffset", language), entry.DriveOffset?.ToString(CultureInfo.InvariantCulture) ?? T("Report_None", language));
        AppendRow(builder, T("Report_CtdbParity", language), BoolLabel(entry.CtdbParityAvailable, language));
        AppendRow(builder, T("Report_CueOrigin", language), Escape(CueOriginLabel(entry.CueOrigin, language)));
        if (!string.IsNullOrWhiteSpace(entry.DbPoweramp.AccurateRipDiscId) ||
            !string.IsNullOrWhiteSpace(entry.DbPoweramp.AccurateRipResult))
        {
            var dbPowerampValue = entry.DbPoweramp.IsAccurate
                ? T("AlbumDetail_DbPowerampVerified", language)
                : entry.DbPoweramp.AccurateRipResult ?? T("Report_None", language);
            AppendRow(builder, T("Report_DbPoweramp", language), Escape(dbPowerampValue));
        }
        builder.AppendLine();

        builder.Append("## ").AppendLine(T("Report_SpectrumSection", language));
        builder.AppendLine();
        builder.Append("| ").Append(T("Report_Field", language)).Append(" | ").Append(T("Report_Value", language)).AppendLine(" |");
        builder.AppendLine("| --- | --- |");
        AppendRow(builder, T("Report_NyquistCutoff", language), string.Format(CultureInfo.InvariantCulture, T("Report_AudioReaches", language), entry.Spectrum.NyquistCutoffHz / 1000.0));
        AppendRow(builder, T("Report_SpectrumVerdict", language), Escape(entry.Spectrum.IsBrickwallSuspected
            ? T("AlbumDetail_PossibleLossy", language)
            : T("AlbumDetail_NotDetected", language)));
        if (!string.IsNullOrWhiteSpace(entry.Spectrum.WorstTrackFilePath))
        {
            AppendRow(builder, T("Report_WorstTrack", language), Escape(Path.GetFileName(entry.Spectrum.WorstTrackFilePath)));
        }
        builder.AppendLine();

        builder.Append("## ").AppendLine(T("Report_OriginSection", language));
        builder.AppendLine();
        builder.Append("| ").Append(T("Report_Field", language)).Append(" | ").Append(T("Report_Value", language)).AppendLine(" |");
        builder.AppendLine("| --- | --- |");
        AppendRow(builder, T("Report_OriginSummary", language), Escape(originSummary));
        if (!string.IsNullOrWhiteSpace(entry.Origin.DigitalDistributor))
        {
            AppendRow(builder, T("Report_Distributor", language), Escape(entry.Origin.DigitalDistributor));
        }
        builder.AppendLine();

        if (entry.Log.HasLog)
        {
            var logResult = entry.Log.ComparedTracks == 0
                ? T("AlbumDetail_LogNoCrc", language)
                : entry.Log.MismatchedTrackNumbers.Count > 0
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        T("AlbumDetail_LogMismatch", language),
                        entry.Log.MismatchedTrackNumbers.Count)
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        T("AlbumDetail_LogVerified", language),
                        entry.Log.MatchedTracks,
                        entry.Log.ComparedTracks);

            builder.Append("## ").AppendLine(T("Report_LogSection", language));
            builder.AppendLine();
            builder.Append("| ").Append(T("Report_Field", language)).Append(" | ").Append(T("Report_Value", language)).AppendLine(" |");
            builder.AppendLine("| --- | --- |");
            AppendRow(builder, T("Report_LogResult", language), Escape(logResult));
            if (!string.IsNullOrWhiteSpace(entry.Log.DriveName))
            {
                AppendRow(builder, T("Report_LogDrive", language), Escape(entry.Log.DriveName));
            }

            AppendRow(
                builder,
                T("Report_LogPath", language),
                Escape(Path.GetFileName(entry.Log.LogPath ?? string.Empty)));
            builder.AppendLine();
        }

        var warnings = entry.Log.Warnings
            .Concat(entry.CueWarnings)
            .Concat(entry.ParserWarnings)
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (warnings.Count > 0)
        {
            builder.Append("## ").AppendLine(T("Report_WarningsSection", language));
            builder.AppendLine();
            foreach (var warning in warnings)
            {
                builder.Append("- ").AppendLine(Escape(warning));
            }
            builder.AppendLine();
        }

        builder.Append("## ").AppendLine(T("Report_DiagnosticsSection", language));
        builder.AppendLine();
        if (entry.Diagnostics.Count == 0)
        {
            builder.AppendLine(T("Report_None", language));
        }
        else
        {
            foreach (var diagnostic in entry.Diagnostics)
            {
                builder.Append("- ").AppendLine(Escape(LocalizeDiagnostic(diagnostic, language)));
            }
        }

        builder.AppendLine();
        AppendFooter(builder, document, language);
        return builder.ToString();
    }

    private void AppendFooter(StringBuilder builder, ReportDocument document, string language)
    {
        builder.AppendLine("---");
        builder.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            T("Report_Footer", language),
            document.AppVersion));
    }

    private static void AppendRow(StringBuilder builder, string field, string value)
    {
        builder.Append("| ").Append(field).Append(" | ").Append(value).AppendLine(" |");
    }

    private string StatusLabel(VerificationStatus? status, string language)
    {
        return status is null
            ? T("Report_None", language)
            : T($"Status_{status.Value}", language);
    }

    private string BoolLabel(bool value, string language)
    {
        return T(value ? "Report_Yes" : "Report_No", language);
    }

    private string CueOriginLabel(CueOrigin origin, string language)
    {
        return T($"CueOrigin_{origin}", language);
    }

    private string LocalizeDiagnostic(string diagnostic, string language)
    {
        return diagnostic is "Classifier_CtdbVerifiedWithArMismatch" or
            "Classifier_DbPowerampAccurateWithArMismatch"
            ? T(diagnostic, language)
            : diagnostic;
    }

    private string T(string key, string language) => _localization.GetString(key, language);

    private static string Escape(string value)
    {
        return value
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }
}
