using System.Buffers.Binary;

namespace ZenithAudio.Core.Audio;

public static class DsfPcmConverter
{
    private const int TargetSampleRate = 88200;
    private const short TargetBitsPerSample = 16;

    public static Task<string> ConvertToTemporaryWavAsync(string sourcePath, string? outputPath = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var input = File.OpenRead(sourcePath);
            var info = ReadDsfInfo(input);
            if (info.Channels <= 0 || info.Channels > 8)
            {
                throw new InvalidOperationException("DSF fallback no pudo detectar canales validos.");
            }

            if (info.SampleRate <= TargetSampleRate || info.SampleRate % TargetSampleRate != 0)
            {
                throw new InvalidOperationException("DSF fallback solo soporta DSD con tasa compatible con 88.2 kHz PCM.");
            }

            outputPath ??= Path.Combine(
                Path.GetTempPath(),
                "ZenithAudio",
                "DsdPcmCache",
                $"ZenithAudio-{Path.GetFileNameWithoutExtension(sourcePath)}-{Guid.NewGuid():N}.wav");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using var output = File.Create(outputPath);
            WritePlaceholderWaveHeader(output);

            input.Position = info.DataOffset;
            var ratio = info.SampleRate / TargetSampleRate;
            var sourceBytes = new byte[info.BlockSizePerChannel * info.Channels];
            var channelStates = new double[info.Channels];
            var pcmBuffer = new byte[info.Channels * sizeof(short)];
            long pcmFrames = 0;

            while (input.Position < info.DataEnd)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var remaining = (int)Math.Min(sourceBytes.Length, info.DataEnd - input.Position);
                var read = input.Read(sourceBytes, 0, remaining);
                if (read <= 0)
                {
                    break;
                }

                var completeChannelBytes = read / info.Channels;
                if (completeChannelBytes <= 0)
                {
                    break;
                }

                var bitsPerChannel = completeChannelBytes * 8;
                var outputFrames = bitsPerChannel / ratio;
                for (var frame = 0; frame < outputFrames; frame++)
                {
                    var bitStart = frame * ratio;
                    for (var channel = 0; channel < info.Channels; channel++)
                    {
                        var channelOffset = channel * info.BlockSizePerChannel;
                        var ones = CountOnes(sourceBytes, channelOffset, bitStart, ratio);
                        var centered = ((ones / (double)ratio) * 2.0) - 1.0;

                        // Small one-pole low-pass: crude, but enough for automatic Realtek PCM fallback.
                        channelStates[channel] = (channelStates[channel] * 0.92) + (centered * 0.08);
                        var sample = (short)Math.Clamp(channelStates[channel] * short.MaxValue, short.MinValue, short.MaxValue);
                        BinaryPrimitives.WriteInt16LittleEndian(pcmBuffer.AsSpan(channel * sizeof(short), sizeof(short)), sample);
                    }

                    output.Write(pcmBuffer, 0, pcmBuffer.Length);
                    pcmFrames++;
                }
            }

            FinalizeWaveHeader(output, info.Channels, pcmFrames);
            return outputPath;
        }, cancellationToken);
    }

    private static DsfInfo ReadDsfInfo(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("DSD "u8))
        {
            throw new InvalidOperationException("El archivo DSD no tiene cabecera DSF valida.");
        }

        var fileSize = ReadUInt64LittleEndian(stream);
        _ = fileSize;
        _ = ReadUInt64LittleEndian(stream);

        var fmt = ReadChunkHeader(stream);
        if (fmt.Id != "fmt ")
        {
            throw new InvalidOperationException("El archivo DSF no contiene bloque fmt esperado.");
        }

        _ = ReadUInt32LittleEndian(stream);
        _ = ReadUInt32LittleEndian(stream);
        _ = ReadUInt32LittleEndian(stream);
        var channels = checked((int)ReadUInt32LittleEndian(stream));
        var sampleRate = checked((int)ReadUInt32LittleEndian(stream));
        _ = ReadUInt32LittleEndian(stream);
        _ = ReadUInt64LittleEndian(stream);
        var blockSize = checked((int)ReadUInt32LittleEndian(stream));
        _ = ReadUInt32LittleEndian(stream);

        stream.Position = fmt.HeaderStart + checked((long)fmt.Size);

        while (stream.Position < stream.Length)
        {
            var chunk = ReadChunkHeader(stream);
            if (chunk.Id == "data")
            {
                return new DsfInfo(channels, sampleRate, blockSize, stream.Position, chunk.HeaderStart + checked((long)chunk.Size));
            }

            stream.Position = chunk.HeaderStart + checked((long)chunk.Size);
        }

        throw new InvalidOperationException("El archivo DSF no contiene bloque de audio data.");
    }

    private static int CountOnes(byte[] source, int channelOffset, int bitStart, int bitCount)
    {
        var ones = 0;
        for (var bit = bitStart; bit < bitStart + bitCount; bit++)
        {
            var value = source[channelOffset + (bit >> 3)];
            ones += (value >> (bit & 7)) & 1;
        }

        return ones;
    }

    private static DsfChunk ReadChunkHeader(Stream stream)
    {
        var headerStart = stream.Position;
        Span<byte> id = stackalloc byte[4];
        stream.ReadExactly(id);
        var size = ReadUInt64LittleEndian(stream);
        return new DsfChunk(System.Text.Encoding.ASCII.GetString(id), size, headerStart);
    }

    private static uint ReadUInt32LittleEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    private static ulong ReadUInt64LittleEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
    }

    private static void WritePlaceholderWaveHeader(Stream stream)
    {
        stream.Write(new byte[44]);
    }

    private static void FinalizeWaveHeader(Stream stream, int channels, long frames)
    {
        var bytesPerSample = TargetBitsPerSample / 8;
        var dataSize = checked((int)(frames * channels * bytesPerSample));
        var byteRate = TargetSampleRate * channels * bytesPerSample;
        var blockAlign = (short)(channels * bytesPerSample);

        stream.Position = 0;
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(TargetSampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(TargetBitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
    }

    private sealed record DsfInfo(int Channels, int SampleRate, int BlockSizePerChannel, long DataOffset, long DataEnd);

    private sealed record DsfChunk(string Id, ulong Size, long HeaderStart);
}
