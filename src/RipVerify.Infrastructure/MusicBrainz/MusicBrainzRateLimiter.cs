using System.Text.Json;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using Serilog;

namespace RipVerify.Infrastructure.MusicBrainz;

public sealed class MusicBrainzRateLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;
    private readonly TimeSpan _minInterval = TimeSpan.FromSeconds(1);

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var elapsed = DateTimeOffset.UtcNow - _lastRequest;
            if (elapsed < _minInterval)
            {
                var delay = _minInterval - elapsed;
                await Task.Delay(delay, cancellationToken);
            }
            _lastRequest = DateTimeOffset.UtcNow;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}

public sealed class MusicBrainzCache
{
    private readonly string _cacheDir;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public MusicBrainzCache()
    {
        _cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RipVerify", "cache", "musicbrainz");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<AlbumMetadata?> GetAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            var filePath = Path.Combine(_cacheDir, $"{key}.json");
            if (!File.Exists(filePath))
            {
                return null;
            }

            var fileInfo = new FileInfo(filePath);
            if (DateTimeOffset.UtcNow - fileInfo.LastWriteTimeUtc > ttl)
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var cached = JsonSerializer.Deserialize<CachedRelease>(json, JsonOptions);
            if (cached == null)
            {
                return null;
            }

            var tracks = cached.Tracks?.Select(t => new TrackMetadata
            {
                Number = t.Number,
                Title = t.Title ?? string.Empty,
                Isrc = t.Isrc,
                Duration = TimeSpan.FromSeconds(t.DurationSeconds),
                FilePath = string.Empty
            }).ToList() ?? new List<TrackMetadata>();

            return new AlbumMetadata
            {
                Album = cached.Album ?? string.Empty,
                Artist = cached.Artist ?? string.Empty,
                MusicBrainzReleaseId = cached.ReleaseId,
                Tracks = tracks
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task SetAsync(string key, AlbumMetadata metadata, CancellationToken cancellationToken)
    {
        try
        {
            var filePath = Path.Combine(_cacheDir, $"{key}.json");
            var cached = new CachedRelease
            {
                ReleaseId = metadata.MusicBrainzReleaseId,
                Album = metadata.Album,
                Artist = metadata.Artist,
                Tracks = metadata.Tracks?.Select(t => new CachedTrack
                {
                    Number = t.Number,
                    Title = t.Title,
                    Isrc = t.Isrc,
                    DurationSeconds = t.Duration.TotalSeconds
                }).ToList()
            };

            var json = JsonSerializer.Serialize(cached, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);
        }
        catch
        {
        }
    }

    private sealed class CachedRelease
    {
        public string? ReleaseId { get; set; }
        public string? Album { get; set; }
        public string? Artist { get; set; }
        public List<CachedTrack>? Tracks { get; set; }
    }

    private sealed class CachedTrack
    {
        public int Number { get; set; }
        public string? Title { get; set; }
        public string? Isrc { get; set; }
        public double DurationSeconds { get; set; }
    }
}
