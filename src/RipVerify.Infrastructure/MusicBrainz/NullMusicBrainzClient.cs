using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;

namespace RipVerify.Infrastructure.MusicBrainz;

public sealed class NullMusicBrainzClient : IMusicBrainzClient
{
    public Task<AlbumMetadata?> FindReleaseByTocAsync(string toc, CancellationToken cancellationToken)
    {
        return Task.FromResult<AlbumMetadata?>(null);
    }

    public Task<AlbumMetadata?> FindReleaseByDiscIdAsync(string discId, CancellationToken cancellationToken)
    {
        return Task.FromResult<AlbumMetadata?>(null);
    }

    public Task<AlbumMetadata?> GetReleaseAsync(string releaseId, CancellationToken cancellationToken)
    {
        return Task.FromResult<AlbumMetadata?>(null);
    }
}
