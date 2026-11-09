using System.Text.RegularExpressions;

namespace RipVerify.Infrastructure.Audio;

public static class MetadataFallback
{
    private static readonly Regex GenericTrackTitlePattern = new(
        @"^(?:\d+[\s._-]+)?(?:track|audio\s*track|pista|audio)\s*\d*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex NumericTitlePattern = new(
        @"^\d{1,3}$",
        RegexOptions.CultureInvariant);

    public static string ResolveAlbum(string? taggedAlbum, string filePath)
    {
        var trimmed = taggedAlbum?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed) && !IsGenericAlbum(trimmed))
        {
            return trimmed;
        }

        return GetFolderName(filePath);
    }

    public static string ResolveTrackTitle(string? taggedTitle, string filePath)
    {
        var fallback = Path.GetFileNameWithoutExtension(filePath);
        var trimmed = taggedTitle?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || IsGenericTrackTitle(trimmed))
        {
            return fallback;
        }

        return trimmed;
    }

    public static bool IsGenericTrackTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        var normalized = Regex.Replace(title.Trim(), @"\s+", " ");
        return GenericTrackTitlePattern.IsMatch(normalized) ||
               NumericTitlePattern.IsMatch(normalized) ||
               normalized.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("untitled", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("sin título", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("sin titulo", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetFolderName(string filePath)
    {
        var directory = Directory.Exists(filePath)
            ? filePath
            : Path.GetDirectoryName(filePath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        var trimmed = directory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return Path.GetFileName(trimmed);
    }

    public static bool IsGenericAlbum(string? album)
    {
        if (string.IsNullOrWhiteSpace(album))
        {
            return true;
        }

        var normalized = album.Trim();
        return normalized.Equals("unknown album", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("álbum desconocido", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("album desconocido", StringComparison.OrdinalIgnoreCase);
    }
}
