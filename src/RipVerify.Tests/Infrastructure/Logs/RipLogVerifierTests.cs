using System.Globalization;
using System.Text;
using FluentAssertions;
using NSubstitute;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using RipVerify.Infrastructure.Audio;
using RipVerify.Infrastructure.Logs;

namespace RipVerify.Tests.Infrastructure.Logs;

public class RipLogVerifierTests
{
    [Fact]
    public async Task MatchesWaveCrcAgainstCopyCrc()
    {
        using var temp = new TempDirectory();
        var wavPath = Path.Combine(temp.Path, "01 - Track One.wav");
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        WritePcmWave(wavPath, data);

        var state = Crc32.Begin();
        state = Crc32.Append(state, data);
        var expected = Crc32.Finish(state).ToString("X8", CultureInfo.InvariantCulture);
        WriteLog(temp.Path, expected, "HL-DT-STBD-RE WH14NS40");

        var verifier = CreateVerifier();
        var album = AlbumWithTrack(wavPath);

        var result = await verifier.VerifyAsync(album, temp.Path, new[] { wavPath }, CancellationToken.None);

        result.HasLog.Should().BeTrue();
        result.AllTracksMatched.Should().BeTrue();
        result.MatchedTracks.Should().Be(1);
        result.IsVirtualDrive.Should().BeFalse();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task VirtualDriveAddsExactWarningEvenWhenCrcMatches()
    {
        using var temp = new TempDirectory();
        var wavPath = Path.Combine(temp.Path, "01 - Track One.wav");
        var data = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        WritePcmWave(wavPath, data);

        var state = Crc32.Begin();
        state = Crc32.Append(state, data);
        var expected = Crc32.Finish(state).ToString("X8", CultureInfo.InvariantCulture);
        WriteLog(temp.Path, expected, "DiscSoftVirtual   Adaptador: 1  ID: 0");

        var verifier = CreateVerifier();
        var result = await verifier.VerifyAsync(
            AlbumWithTrack(wavPath),
            temp.Path,
            new[] { wavPath },
            CancellationToken.None);

        result.AllTracksMatched.Should().BeTrue();
        result.IsVirtualDrive.Should().BeTrue();
        result.Warnings.Should().Contain(RipLogVerifier.VirtualDriveWarning);
    }

    [Fact]
    public async Task MismatchedCrcIsReported()
    {
        using var temp = new TempDirectory();
        var wavPath = Path.Combine(temp.Path, "01 - Track One.wav");
        WritePcmWave(wavPath, new byte[] { 0x01, 0x02, 0x03, 0x04 });
        WriteLog(temp.Path, "DEADBEEF", "HL-DT-STBD-RE WH14NS40");

        var verifier = CreateVerifier();
        var result = await verifier.VerifyAsync(
            AlbumWithTrack(wavPath),
            temp.Path,
            new[] { wavPath },
            CancellationToken.None);

        result.AllTracksMatched.Should().BeFalse();
        result.MismatchedTrackNumbers.Should().Contain(1);
        result.Warnings.Should().Contain(warning => warning.Contains("no coincide"));
    }

    private static RipLogVerifier CreateVerifier()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.Current.Returns(new AppSettings());
        var validator = Substitute.For<IFileValidator>();
        return new RipLogVerifier(new AudioCrc32Reader(settings, validator));
    }

    private static AlbumMetadata AlbumWithTrack(string path)
    {
        return new AlbumMetadata
        {
            Album = "Test",
            Tracks = new[]
            {
                new TrackMetadata { Number = 1, Title = "Track One", FilePath = path }
            }
        };
    }

    private static void WriteLog(string folder, string crc, string drive)
    {
        var content = new StringBuilder()
            .AppendLine("Exact Audio Copy V1.5 from 20. February 2020")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"Used drive  : {drive}")
            .AppendLine()
            .AppendLine("Track  1")
            .AppendLine("     Filename 01 - Track One.wav")
            .AppendLine(CultureInfo.InvariantCulture, $"     Copy CRC {crc}")
            .ToString();
        File.WriteAllText(Path.Combine(folder, "album.log"), content, Encoding.UTF8);
    }

    private static void WritePcmWave(string path, byte[] data)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + data.Length);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(44100);
        writer.Write(44100 * 2 * 2);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(data.Length);
        writer.Write(data);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "RipVerifyTests-" + Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
