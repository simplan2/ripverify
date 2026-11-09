using System.Buffers.Binary;
using System.Diagnostics;
using RipVerify.Core.Services;
using Serilog;
using DiagProcess = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace RipVerify.Infrastructure.Audio;

public sealed class AudioCrc32Reader
{
    private const int BufferSize = 64 * 1024;
    private readonly ISettingsService _settingsService;
    private readonly IFileValidator _fileValidator;

    public AudioCrc32Reader(ISettingsService settingsService, IFileValidator fileValidator)
    {
        _settingsService = settingsService;
        _fileValidator = fileValidator;
    }

    public async Task<uint?> ComputeAsync(string audioFilePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(audioFilePath) || !File.Exists(audioFilePath))
        {
            return null;
        }

        var waveCrc = await TryComputeWaveCrcAsync(audioFilePath, cancellationToken).ConfigureAwait(false);
        if (waveCrc.HasValue)
        {
            return waveCrc;
        }

        return await ComputeWithFfmpegAsync(audioFilePath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<uint?> TryComputeWaveCrcAsync(string audioFilePath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                audioFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var header = new byte[12];
            if (!await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
                !header.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            {
                return null;
            }

            var isPcm44kStereo16 = false;
            long dataOffset = -1;
            long dataLength = 0;
            var chunkHeader = new byte[8];

            while (stream.Position + 8 <= stream.Length)
            {
                if (!await ReadExactlyAsync(stream, chunkHeader, cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                var chunkId = chunkHeader.AsSpan(0, 4);
                var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.AsSpan(4, 4));

                if (chunkId.SequenceEqual("fmt "u8))
                {
                    if (chunkSize < 16)
                    {
                        return null;
                    }

                    var format = new byte[16];
                    if (!await ReadExactlyAsync(stream, format, cancellationToken).ConfigureAwait(false))
                    {
                        return null;
                    }

                    var audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(0, 2));
                    var channels = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(2, 2));
                    var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(format.AsSpan(4, 4));
                    var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(14, 2));
                    isPcm44kStereo16 = audioFormat == 1 &&
                                       channels == 2 &&
                                       sampleRate == 44100 &&
                                       bitsPerSample == 16;

                    var remaining = (long)chunkSize - 16 + (chunkSize & 1);
                    if (remaining > 0)
                    {
                        stream.Seek(remaining, SeekOrigin.Current);
                    }
                }
                else if (chunkId.SequenceEqual("data"u8))
                {
                    dataOffset = stream.Position;
                    dataLength = Math.Min(chunkSize, stream.Length - stream.Position);
                    break;
                }
                else
                {
                    var skip = (long)chunkSize + (chunkSize & 1);
                    if (skip <= 0 || stream.Position + skip > stream.Length)
                    {
                        return null;
                    }

                    stream.Seek(skip, SeekOrigin.Current);
                }
            }

            if (!isPcm44kStereo16 || dataOffset < 0 || dataLength <= 0)
            {
                return null;
            }

            stream.Position = dataOffset;
            var state = Crc32.Begin();
            var buffer = new byte[BufferSize];
            var remainingBytes = dataLength;

            while (remainingBytes > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var toRead = (int)Math.Min(buffer.Length, remainingBytes);
                var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                state = Crc32.Append(state, buffer.AsSpan(0, read));
                remainingBytes -= read;
            }

            return Crc32.Finish(state);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read WAV PCM for CRC: {AudioFilePath}", audioFilePath);
            return null;
        }
    }

    private async Task<uint?> ComputeWithFfmpegAsync(string audioFilePath, CancellationToken cancellationToken)
    {
        var ffmpegPath = _fileValidator.FindFfmpeg(_settingsService.Current.FfmpegPath);
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            Log.Warning("FFmpeg is not available; cannot compute audio CRC for {AudioFilePath}", audioFilePath);
            return null;
        }

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
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(audioFilePath);
        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("0:a:0");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("s16le");
        startInfo.ArgumentList.Add("-c:a");
        startInfo.ArgumentList.Add("pcm_s16le");
        startInfo.ArgumentList.Add("-ar");
        startInfo.ArgumentList.Add("44100");
        startInfo.ArgumentList.Add("-ac");
        startInfo.ArgumentList.Add("2");
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
                return null;
            }

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var state = Crc32.Begin();
            var buffer = new byte[BufferSize];
            var totalRead = 0;

            while (true)
            {
                var read = await process.StandardOutput.BaseStream
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                state = Crc32.Append(state, buffer.AsSpan(0, read));
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0 || totalRead == 0)
            {
                Log.Warning(
                    "FFmpeg failed to compute audio CRC for {AudioFilePath}. ExitCode={ExitCode} Error={Error}",
                    audioFilePath,
                    process.ExitCode,
                    stderr.Trim());
                return null;
            }

            return Crc32.Finish(state);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to compute audio CRC for {AudioFilePath}", audioFilePath);
            return null;
        }
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}
