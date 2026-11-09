using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RipVerify.Core.Models;

namespace RipVerify.Infrastructure.Logs;

public sealed class LogFileParser
{
    private static readonly Regex TrackRegex = new(
        @"^\s*(?:Track|Pista)\s+(\d+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FileNameRegex = new(
        @"^\s*(?:Filename|File Name|Nombre de Archivo|Nombre del archivo)\s*:?\s*(?<name>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CrcRegex = new(
        @"^\s*(?<kind>Test CRC|Copy CRC|Probar CRC|Copiar CRC|CRC32)\s*[:=]?\s*(?<crc>[0-9A-Fa-f]{8})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DriveRegex = new(
        @"^\s*(?:Used drive|Usar unidad|Drive)\s*:\s*(?<name>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] LineSeparators = { "\r\n", "\n", "\r" };
    private static readonly string[] VirtualDriveMarkers =
    {
        "DiscSoftVirtual",
        "DAEMON Tools",
        "Virtual CloneDrive",
        "VirtualDVD",
        "Virtual Drive"
    };

    public static async Task<RipLogDocument?> ParseFileAsync(string logFilePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(logFilePath, cancellationToken).ConfigureAwait(false);
        var content = Decode(bytes);
        return Parse(content, logFilePath);
    }

    public static RipLogDocument Parse(string content, string logFilePath)
    {
        var tracks = new List<RipLogTrack>();
        string? creator = null;
        string? driveName = null;
        int? currentTrack = null;
        string? currentFileName = null;
        string? currentCopyCrc = null;
        string? currentTestCrc = null;

        void CommitTrack()
        {
            if (currentTrack is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(currentCopyCrc) || !string.IsNullOrWhiteSpace(currentTestCrc))
            {
                tracks.Add(new RipLogTrack
                {
                    Number = currentTrack.Value,
                    FileName = currentFileName,
                    CopyCrc = NormalizeCrc(currentCopyCrc),
                    TestCrc = NormalizeCrc(currentTestCrc)
                });
            }

            currentTrack = null;
            currentFileName = null;
            currentCopyCrc = null;
            currentTestCrc = null;
        }

        foreach (var rawLine in content.Split(LineSeparators, StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            creator ??= DetectCreator(line);

            var driveMatch = DriveRegex.Match(line);
            if (driveMatch.Success && string.IsNullOrWhiteSpace(driveName))
            {
                driveName = NormalizeDriveName(driveMatch.Groups["name"].Value);
                continue;
            }

            var trackMatch = TrackRegex.Match(line);
            if (trackMatch.Success && int.TryParse(trackMatch.Groups[1].Value, out var trackNumber))
            {
                CommitTrack();
                currentTrack = trackNumber;
                continue;
            }

            if (currentTrack is null)
            {
                continue;
            }

            var fileNameMatch = FileNameRegex.Match(line);
            if (fileNameMatch.Success)
            {
                currentFileName = fileNameMatch.Groups["name"].Value.Trim();
                continue;
            }

            var crcMatch = CrcRegex.Match(line);
            if (crcMatch.Success)
            {
                var kind = crcMatch.Groups["kind"].Value;
                var crc = crcMatch.Groups["crc"].Value.ToUpperInvariant();
                if (kind.Contains("Test", StringComparison.OrdinalIgnoreCase) ||
                    kind.Contains("Probar", StringComparison.OrdinalIgnoreCase))
                {
                    currentTestCrc = crc;
                }
                else
                {
                    currentCopyCrc = crc;
                }
            }
        }

        CommitTrack();

        var isVirtualDrive = !string.IsNullOrWhiteSpace(driveName) &&
                             VirtualDriveMarkers.Any(marker =>
                                 driveName.Contains(marker, StringComparison.OrdinalIgnoreCase));

        return new RipLogDocument
        {
            FilePath = logFilePath,
            Creator = creator,
            DriveName = driveName,
            IsVirtualDrive = isVirtualDrive,
            Tracks = tracks
        };
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static string? DetectCreator(string line)
    {
        if (line.Contains("Exact Audio Copy", StringComparison.OrdinalIgnoreCase))
        {
            return "Exact Audio Copy";
        }

        if (line.Contains("dBpoweramp", StringComparison.OrdinalIgnoreCase))
        {
            return "dBpoweramp";
        }

        return null;
    }

    private static string NormalizeDriveName(string value)
    {
        var name = value.Trim();
        var adapterIndex = name.IndexOf("Adapter", StringComparison.OrdinalIgnoreCase);
        var spanishAdapterIndex = name.IndexOf("Adaptador", StringComparison.OrdinalIgnoreCase);
        if (adapterIndex >= 0 && spanishAdapterIndex >= 0)
        {
            adapterIndex = Math.Min(adapterIndex, spanishAdapterIndex);
        }
        else if (spanishAdapterIndex >= 0)
        {
            adapterIndex = spanishAdapterIndex;
        }

        if (adapterIndex >= 0)
        {
            name = name[..adapterIndex];
        }

        return Regex.Replace(name, @"\s+", " ").Trim();
    }

    private static string? NormalizeCrc(string? crc)
        => string.IsNullOrWhiteSpace(crc) ? null : crc.Trim().ToUpperInvariant();
}
