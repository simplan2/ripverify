using System.Text.Json;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using Serilog;

namespace RipVerify.Infrastructure.Calibration;

public sealed class JsonCalibrationCorpus : ICalibrationCorpus
{
    private readonly string _manifestPath;
    private static readonly JsonSerializerOptions JsonReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    public JsonCalibrationCorpus(string? manifestPath = null)
    {
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            _manifestPath = manifestPath;
            return;
        }

        var baseDir = AppContext.BaseDirectory;
        var toolsDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "tools", "calibration-corpus"));
        if (!Directory.Exists(toolsDir))
        {
            // Try relative to solution
            toolsDir = Path.Combine("tools", "calibration-corpus");
        }
        Directory.CreateDirectory(toolsDir);
        _manifestPath = Path.Combine(toolsDir, "manifest.json");
    }

    public async Task<CalibrationCorpus> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_manifestPath))
            {
                return new CalibrationCorpus();
            }

            var json = await File.ReadAllTextAsync(_manifestPath, cancellationToken);
            var data = JsonSerializer.Deserialize<CalibrationCorpusData>(json, JsonReadOptions);

            if (data == null)
            {
                return new CalibrationCorpus();
            }

            var entries = new List<CalibrationEntry>();
            foreach (var entry in data.Entries ?? new List<CalibrationEntryData>())
            {
                var origin = entry.ExpectedOrigin ?? CalibrationEntry.UnknownOrigin;
                if (!CalibrationEntry.AllowedOrigins.Contains(origin))
                {
                    Log.Warning(
                        "Calibration_UnknownOriginCategory: {ExpectedOrigin} for {FilePath}; entry excluded from calibration",
                        origin,
                        entry.FilePath);
                    continue;
                }

                entries.Add(new CalibrationEntry
                {
                    FilePath = entry.FilePath ?? string.Empty,
                    ExpectedOrigin = origin,
                    ObservedBrickwallScore = entry.ObservedBrickwallScore,
                    ObservedNyquistCutoffHz = entry.ObservedNyquistCutoffHz,
                    ObservedRolloffSlopeDbPerKHz = entry.ObservedRolloffSlopeDbPerKHz,
                    ObservedSpectralFlatness = entry.ObservedSpectralFlatness,
                    Notes = entry.Notes ?? string.Empty,
                    TaggedAt = entry.TaggedAt
                });
            }

            return new CalibrationCorpus
            {
                Entries = entries,
                CalibratedBrickwallThreshold = data.CalibratedBrickwallThreshold,
                LastCalibratedAt = data.LastCalibratedAt
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load calibration corpus");
            return new CalibrationCorpus();
        }
    }

    public async Task SaveEntryAsync(CalibrationEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            var corpus = await LoadAsync(cancellationToken);
            var entries = corpus.Entries.ToList();
            entries.Add(entry);

            var data = new CalibrationCorpusData
            {
                Entries = entries.Select(e => new CalibrationEntryData
                {
                    FilePath = e.FilePath,
                    ExpectedOrigin = e.ExpectedOrigin,
                    ObservedBrickwallScore = e.ObservedBrickwallScore,
                    ObservedNyquistCutoffHz = e.ObservedNyquistCutoffHz,
                    ObservedRolloffSlopeDbPerKHz = e.ObservedRolloffSlopeDbPerKHz,
                    ObservedSpectralFlatness = e.ObservedSpectralFlatness,
                    Notes = e.Notes,
                    TaggedAt = e.TaggedAt
                }).ToList(),
                CalibratedBrickwallThreshold = corpus.CalibratedBrickwallThreshold,
                LastCalibratedAt = corpus.LastCalibratedAt
            };

            var json = JsonSerializer.Serialize(data, JsonWriteOptions);
            await File.WriteAllTextAsync(_manifestPath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save calibration entry");
        }
    }

    private sealed class CalibrationCorpusData
    {
        public List<CalibrationEntryData>? Entries { get; set; }
        public double? CalibratedBrickwallThreshold { get; set; }
        public DateTimeOffset? LastCalibratedAt { get; set; }
    }

    private sealed class CalibrationEntryData
    {
        public string? FilePath { get; set; }
        public string? ExpectedOrigin { get; set; }
        public double ObservedBrickwallScore { get; set; }
        public double ObservedNyquistCutoffHz { get; set; }
        public double ObservedRolloffSlopeDbPerKHz { get; set; }
        public double ObservedSpectralFlatness { get; set; }
        public string? Notes { get; set; }
        public DateTimeOffset TaggedAt { get; set; }
    }
}
