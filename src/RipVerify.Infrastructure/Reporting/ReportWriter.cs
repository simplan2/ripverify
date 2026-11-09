using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;

namespace RipVerify.Infrastructure.Reporting;

public sealed class ReportWriter : IReportWriter
{
    public const string JsonFileName = "ripverify-report.json";
    public const string MarkdownFileName = "ripverify-report.md";
    public const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly Action<ILogger, string, Exception?> _logJsonWriteFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(WriteJsonAsync)),
            "Failed to write JSON report to {AlbumFolder}");

    private static readonly Action<ILogger, string, Exception?> _logMarkdownWriteFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2, nameof(WriteMarkdownAsync)),
            "Failed to write Markdown report to {AlbumFolder}");

    private static readonly Action<ILogger, int, int, Exception?> _logUnsupportedSchema =
        LoggerMessage.Define<int, int>(
            LogLevel.Warning,
            new EventId(3, nameof(ValidateSchema)),
            "Unsupported report schema version {SchemaVersion}; expected {ExpectedVersion}");

    private static readonly Action<ILogger, string, string, Exception?> _logFolderUnavailable =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(4, nameof(IsFolderAvailable)),
            "Cannot write {Format} report: album folder {AlbumFolder} does not exist");

    private readonly MarkdownReportRenderer _renderer;
    private readonly ILogger<ReportWriter> _logger;

    public ReportWriter(MarkdownReportRenderer renderer, ILogger<ReportWriter> logger)
    {
        _renderer = renderer;
        _logger = logger;
    }

    public async Task WriteJsonAsync(string albumFolder, ReportDocument document, CancellationToken cancellationToken)
    {
        if (!ValidateSchema(document))
        {
            return;
        }

        if (!IsFolderAvailable(albumFolder, "JSON"))
        {
            return;
        }

        var path = Path.Combine(albumFolder, JsonFileName);

        try
        {
            var json = JsonSerializer.Serialize(document, JsonOptions);
            await File.WriteAllTextAsync(path, json, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logJsonWriteFailed(_logger, albumFolder, ex);
        }
    }

    public async Task WriteMarkdownAsync(string albumFolder, ReportDocument document, bool forceEnglish, CancellationToken cancellationToken)
    {
        if (!ValidateSchema(document))
        {
            return;
        }

        if (!IsFolderAvailable(albumFolder, "Markdown"))
        {
            return;
        }

        var path = Path.Combine(albumFolder, MarkdownFileName);

        try
        {
            var markdown = _renderer.Render(document, forceEnglish);
            await File.WriteAllTextAsync(path, markdown, Utf8NoBom, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logMarkdownWriteFailed(_logger, albumFolder, ex);
        }
    }

    private bool ValidateSchema(ReportDocument document)
    {
        if (document.SchemaVersion == CurrentSchemaVersion)
        {
            return true;
        }

        _logUnsupportedSchema(_logger, document.SchemaVersion, CurrentSchemaVersion, null);
        return false;
    }

    private bool IsFolderAvailable(string albumFolder, string format)
    {
        if (!string.IsNullOrWhiteSpace(albumFolder) && Directory.Exists(albumFolder))
        {
            return true;
        }

        _logFolderUnavailable(_logger, format, albumFolder, null);
        return false;
    }
}
