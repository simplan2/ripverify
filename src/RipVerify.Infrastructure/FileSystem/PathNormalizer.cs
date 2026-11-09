using System.Globalization;
using System.Text;

namespace RipVerify.Infrastructure.FileSystem;

public static class PathNormalizer
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFullPath(path.Trim());
    }

    public static bool IsAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return Path.IsPathRooted(path);
    }

    public static string MakeRelative(string basePath, string targetPath)
    {
        var baseFull = Normalize(basePath);
        var targetFull = Normalize(targetPath);
        if (string.IsNullOrEmpty(baseFull) || string.IsNullOrEmpty(targetFull))
        {
            return targetPath;
        }

        var baseUri = new Uri(AppendDirectorySeparatorChar(baseFull));
        var targetUri = new Uri(targetFull);
        var relativeUri = baseUri.MakeRelativeUri(targetUri);
        var relative = Uri.UnescapeDataString(relativeUri.ToString());
        return relative.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparatorChar(string path)
    {
        if (!path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) &&
            !path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            return path + Path.DirectorySeparatorChar;
        }

        return path;
    }
}
