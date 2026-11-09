using System.Diagnostics;

namespace RipVerify.Tests.Infrastructure.Audio;

internal static class AudioFixtures
{
    private const string NoiseSource =
        "anoisesrc=color=white:amplitude=0.5:duration=10:sample_rate=44100";

    private static readonly Lazy<string?> Ffmpeg = new(FindFfmpeg, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string? FfmpegPath => Ffmpeg.Value;

    public static string CreateGradualRolloff(string directory)
        => Create(directory, "gradual-rolloff-15khz.flac", "lowpass=f=15000:poles=2,lowpass=f=15000:poles=2");

    public static string CreateMp3LikeBrickwall(string directory)
        => Create(
            directory,
            "mp3-like-brickwall-16khz.flac",
            "afftfilt=real='if(lt(b\\,1486)\\,re\\,0)':imag='if(lt(b\\,1486)\\,im\\,0)'");

    public static string CreateMasteringFilter(string directory)
        => Create(
            directory,
            "mastering-filter-19700hz.flac",
            "afftfilt=real='if(lt(b\\,1830)\\,re\\,0)':imag='if(lt(b\\,1830)\\,im\\,0)'");

    public static string CreateFullSpectrum(string directory)
        => Create(directory, "full-spectrum-22khz.flac", null);

    private static string Create(string directory, string fileName, string? audioFilter)
    {
        var ffmpeg = FfmpegPath ?? throw new InvalidOperationException(
            "FFmpeg was not found. Expected build/ffmpeg/ffmpeg.exe in the repository.");

        var outputPath = Path.Combine(directory, fileName);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var arguments = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-f",
            "lavfi",
            "-i",
            NoiseSource
        };

        if (!string.IsNullOrWhiteSpace(audioFilter))
        {
            arguments.Add("-af");
            arguments.Add(audioFilter);
        }

        arguments.AddRange(new[] { "-ac", "1", "-c:a", "flac", outputPath });

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start FFmpeg to generate test fixtures.");
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"FFmpeg failed generating {fileName} (exit {process.ExitCode}): {stderr}");
        }

        return outputPath;
    }

    private static string? FindFfmpeg()
    {
        var environmentPath = Environment.GetEnvironmentVariable("RIPVERIFY_FFMPEG");
        if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath))
        {
            return environmentPath;
        }

        foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var directory = new DirectoryInfo(root);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "build", "ffmpeg", "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
