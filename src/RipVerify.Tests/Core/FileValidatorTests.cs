namespace RipVerify.Tests.Core;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RipVerify.Core.Services;
using Xunit;

public class FileValidatorTests
{
    [Fact]
    public void ConfiguredPathWinsOverBundledBinary()
    {
        var root = Directory.CreateTempSubdirectory("ripverify-ffmpeg-");
        try
        {
            var configured = Path.Combine(root.FullName, "custom", "ffmpeg.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(configured)!);
            File.WriteAllText(configured, string.Empty);

            var bundled = Path.Combine(root.FullName, "app", "ffmpeg", "ffmpeg.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
            File.WriteAllText(bundled, string.Empty);

            var validator = new FileValidator(NullLogger<FileValidator>.Instance)
            {
                BaseDirectory = Path.Combine(root.FullName, "app"),
                ProgramFilesRoot = Path.Combine(root.FullName, "programfiles")
            };

            validator.FindFfmpeg(configured).Should().Be(configured);
            validator.IsFfmpegAvailable(configured).Should().BeTrue();
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void BundledBinaryIsUsedWhenNoConfiguredPath()
    {
        var root = Directory.CreateTempSubdirectory("ripverify-ffmpeg-");
        try
        {
            var bundled = Path.Combine(root.FullName, "ffmpeg", "ffmpeg.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
            File.WriteAllText(bundled, string.Empty);

            var validator = new FileValidator(NullLogger<FileValidator>.Instance)
            {
                BaseDirectory = root.FullName,
                ProgramFilesRoot = Path.Combine(root.FullName, "programfiles")
            };

            validator.FindFfmpeg(null).Should().Be(bundled);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Fact]
    public void InstalledBinaryIsUsedWhenNotBundled()
    {
        var root = Directory.CreateTempSubdirectory("ripverify-ffmpeg-");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var installed = Path.Combine(root.FullName, "RipVerify", "ffmpeg", "ffmpeg.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
            File.WriteAllText(installed, string.Empty);
            var emptyPath = Path.Combine(root.FullName, "empty-path");
            Directory.CreateDirectory(emptyPath);
            Environment.SetEnvironmentVariable("PATH", emptyPath);

            var validator = new FileValidator(NullLogger<FileValidator>.Instance)
            {
                BaseDirectory = Path.Combine(root.FullName, "app"),
                ProgramFilesRoot = root.FullName
            };

            validator.FindFfmpeg(null).Should().Be(installed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            root.Delete(true);
        }
    }

    [Fact]
    public void PathFallbackIsUsedWhenNothingElseExists()
    {
        var root = Directory.CreateTempSubdirectory("ripverify-ffmpeg-");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var onPath = Path.Combine(root.FullName, "tools", "ffmpeg.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(onPath)!);
            File.WriteAllText(onPath, string.Empty);
            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(onPath));

            var validator = new FileValidator(NullLogger<FileValidator>.Instance)
            {
                BaseDirectory = Path.Combine(root.FullName, "app"),
                ProgramFilesRoot = Path.Combine(root.FullName, "programfiles")
            };

            validator.FindFfmpeg(string.Empty).Should().Be(onPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            root.Delete(true);
        }
    }

    [Fact]
    public void ReturnsNullWhenNoBinaryExists()
    {
        var root = Directory.CreateTempSubdirectory("ripverify-ffmpeg-");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var emptyPath = Path.Combine(root.FullName, "empty-path");
            Directory.CreateDirectory(emptyPath);
            Environment.SetEnvironmentVariable("PATH", emptyPath);

            var validator = new FileValidator(NullLogger<FileValidator>.Instance)
            {
                BaseDirectory = Path.Combine(root.FullName, "app"),
                ProgramFilesRoot = Path.Combine(root.FullName, "programfiles")
            };

            validator.FindFfmpeg(null).Should().BeNull();
            validator.IsFfmpegAvailable(null).Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            root.Delete(true);
        }
    }
}
