namespace RipVerify.Core.Abstractions;

using RipVerify.Core.Models;

public interface IRipLogVerifier
{
    Task<RipLogVerification> VerifyAsync(
        AlbumMetadata album,
        string? albumFolder,
        IReadOnlyList<string> audioFilePaths,
        CancellationToken cancellationToken);
}
