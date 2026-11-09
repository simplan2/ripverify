using DiagProcess = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using RipVerify.Core.Services;
using Serilog;

namespace RipVerify.Infrastructure.Audio;

public sealed class FfmpegPcmReader
{
    private readonly ISettingsService _settingsService;
    private readonly IFileValidator _fileValidator;

    public FfmpegPcmReader(ISettingsService settingsService, IFileValidator fileValidator)
    {
        _settingsService = settingsService;
        _fileValidator = fileValidator;
    }

    public async Task<float[]?> ReadMono44100Async(string audioFilePath, CancellationToken cancellationToken)
    {
        var ffmpegPath = _fileValidator.FindFfmpeg(_settingsService.Current.FfmpegPath);
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            Log.Warning("FFmpeg is not available; cannot decode {AudioFilePath}", audioFilePath);
            return null;
        }

        if (!File.Exists(audioFilePath))
        {
            Log.Warning("Audio file not found for PCM decoding: {AudioFilePath}", audioFilePath);
            return null;
        }

        Log.Debug("Decoding PCM with FFmpeg: FfmpegPath={FfmpegPath} AudioFilePath={AudioFilePath}", ffmpegPath, audioFilePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(audioFilePath);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("f32le");
        startInfo.ArgumentList.Add("-acodec");
        startInfo.ArgumentList.Add("pcm_f32le");
        startInfo.ArgumentList.Add("-ar");
        startInfo.ArgumentList.Add("44100");
        startInfo.ArgumentList.Add("-ac");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-");

        using var process = new DiagProcess { StartInfo = startInfo };
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        });

        try
        {
            if (!process.Start())
            {
                Log.Warning("FFmpeg process could not be started: FfmpegPath={FfmpegPath} AudioFilePath={AudioFilePath}", ffmpegPath, audioFilePath);
                return null;
            }

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            using var buffer = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (process.ExitCode != 0)
            {
                Log.Warning(
                    "FFmpeg failed to decode {AudioFilePath} using {FfmpegPath}. ExitCode={ExitCode} Error={Error}",
                    audioFilePath,
                    ffmpegPath,
                    process.ExitCode,
                    stderr.Trim());
                return null;
            }

            var bytes = buffer.ToArray();
            var sampleCount = bytes.Length / sizeof(float);
            if (sampleCount == 0)
            {
                Log.Warning(
                    "FFmpeg produced no PCM samples for {AudioFilePath} using {FfmpegPath}. Stderr={Error}",
                    audioFilePath,
                    ffmpegPath,
                    stderr.Trim());
                return Array.Empty<float>();
            }

            var samples = new float[sampleCount];
            Buffer.BlockCopy(bytes, 0, samples, 0, sampleCount * sizeof(float));
            Log.Debug(
                "FFmpeg decoded PCM: AudioFilePath={AudioFilePath} Samples={SampleCount} DurationSeconds={Duration:F1} Stderr={Error}",
                audioFilePath,
                sampleCount,
                sampleCount / 44100.0,
                stderr.Trim());
            return samples;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to decode PCM for {AudioFilePath} with {FfmpegPath}", audioFilePath, ffmpegPath);
            return null;
        }
    }
}

