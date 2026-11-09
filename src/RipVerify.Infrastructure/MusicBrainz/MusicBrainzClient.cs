using System.Net.Http;
using System.Text.Json;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using Serilog;

namespace RipVerify.Infrastructure.MusicBrainz;

public sealed class MusicBrainzClient : IMusicBrainzClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly MusicBrainzRateLimiter _rateLimiter;
    private readonly MusicBrainzCache _cache;
    private readonly TimeSpan _cacheTtl = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public MusicBrainzClient(MusicBrainzRateLimiter rateLimiter, MusicBrainzCache cache, ISettingsService settingsService)
    {
        _httpClient = new HttpClient();
        _rateLimiter = rateLimiter;
        _cache = cache;

        var configuredUserAgent = settingsService.Current.MusicBrainzUserAgent;
        var userAgent = AppSettings.IsValidMusicBrainzUserAgent(configuredUserAgent)
            ? configuredUserAgent
            : AppSettings.DefaultMusicBrainzUserAgent;

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _httpClient.BaseAddress = new Uri("https://musicbrainz.org");
    }

    public async Task<AlbumMetadata?> FindReleaseByTocAsync(string toc, CancellationToken cancellationToken)
    {
        var cacheKey = $"toc:{toc}";
        var cached = await _cache.GetAsync(cacheKey, _cacheTtl, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        var url = $"/ws/2/release/?query=toc:{Uri.EscapeDataString(toc)}&fmt=json";
        var result = await SendWithRetryAsync(url, cancellationToken);
        if (result == null)
        {
            return null;
        }

        var release = result.RootElement.TryGetProperty("releases", out var releases) && releases.GetArrayLength() > 0 
            ? releases[0] 
            : default;
        
        if (release.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var metadata = MapReleaseStatic(release);
        if (metadata != null)
        {
            await _cache.SetAsync(cacheKey, metadata, cancellationToken);
        }
        return metadata;
    }

    public async Task<AlbumMetadata?> FindReleaseByDiscIdAsync(string discId, CancellationToken cancellationToken)
    {
        var cacheKey = $"discid:{discId}";
        var cached = await _cache.GetAsync(cacheKey, _cacheTtl, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        var url = $"/ws/2/discid/{Uri.EscapeDataString(discId)}?fmt=json&inc=recordings+artist-credits";
        var result = await SendWithRetryAsync(url, cancellationToken);
        if (result == null)
        {
            return null;
        }

        if (result.RootElement.TryGetProperty("releases", out var releases) && releases.GetArrayLength() > 0)
        {
            var release = releases[0];
            var metadata = MapReleaseStatic(release);
            if (metadata != null)
            {
                await _cache.SetAsync(cacheKey, metadata, cancellationToken);
            }
            return metadata;
        }

        return null;
    }

    public async Task<AlbumMetadata?> GetReleaseAsync(string releaseId, CancellationToken cancellationToken)
    {
        var cacheKey = $"release:{releaseId}";
        var cached = await _cache.GetAsync(cacheKey, _cacheTtl, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        var url = $"/ws/2/release/{Uri.EscapeDataString(releaseId)}?fmt=json&inc=recordings+artist-credits+labels";
        var result = await SendWithRetryAsync(url, cancellationToken);
        if (result == null)
        {
            return null;
        }

        var metadata = MapReleaseStatic(result.RootElement);
        if (metadata != null)
        {
            await _cache.SetAsync(cacheKey, metadata, cancellationToken);
        }
        return metadata;
    }

    private async Task<JsonDocument?> SendWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        int retries = 0;
        while (retries < 3)
        {
            await _rateLimiter.WaitAsync(cancellationToken);
            try
            {
                var response = await _httpClient.GetAsync(url, cancellationToken);
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || 
                    response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    retries++;
                    if (response.Headers.RetryAfter?.Delta.HasValue == true)
                    {
                        await Task.Delay(response.Headers.RetryAfter.Delta.Value, cancellationToken);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)), cancellationToken);
                    }
                    continue;
                }

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    Log.Warning("MusicBrainz returned {StatusCode}", response.StatusCode);
                    return null;
                }

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonDocument.Parse(content);
            }
            catch (HttpRequestException ex)
            {
                Log.Warning(ex, "MusicBrainz request failed");
                retries++;
                if (retries >= 3) return null;
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retries)), cancellationToken);
            }
        }
        return null;
    }

    private static AlbumMetadata? MapReleaseStatic(JsonElement release)
    {
        try
        {
            var title = release.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            string artist = string.Empty;
            if (release.TryGetProperty("artist-credit", out var ac) && ac.GetArrayLength() > 0)
            {
                if (ac[0].TryGetProperty("name", out var an)) artist = an.GetString() ?? string.Empty;
            }

            string? releaseId = release.TryGetProperty("id", out var rid) ? rid.GetString() : null;

            var tracks = new List<TrackMetadata>();
            if (release.TryGetProperty("media", out var media) && media.GetArrayLength() > 0)
            {
                var medium = media[0];
                if (medium.TryGetProperty("tracks", out var trackList))
                {
                    int num = 1;
                    foreach (var tr in trackList.EnumerateArray())
                    {
                        var trackTitle = tr.TryGetProperty("title", out var tt) ? tt.GetString() ?? string.Empty : string.Empty;
                        string? isrc = null;
                        if (tr.TryGetProperty("recording", out var rec) && rec.TryGetProperty("isrcs", out var isrcs) && isrcs.GetArrayLength() > 0)
                        {
                            isrc = isrcs[0].GetString();
                        }
                        TimeSpan duration = TimeSpan.Zero;
                        if (tr.TryGetProperty("length", out var len) && len.ValueKind == JsonValueKind.Number)
                        {
                            duration = TimeSpan.FromMilliseconds(len.GetInt64());
                        }

                        tracks.Add(new TrackMetadata
                        {
                            Number = num++,
                            Title = trackTitle,
                            Isrc = isrc,
                            Duration = duration,
                            FilePath = string.Empty
                        });
                    }
                }
            }

            return new AlbumMetadata
            {
                Album = title,
                Artist = artist,
                MusicBrainzReleaseId = releaseId,
                Tracks = tracks
            };
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
