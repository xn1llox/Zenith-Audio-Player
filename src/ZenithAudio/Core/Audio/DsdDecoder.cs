using System.Buffers.Binary;
using NAudio.Wave;

namespace ZenithAudio.Core.Audio;

public sealed record DsdStreamMetadata(
    int Channels,
    int SampleRate,
    ulong SampleCount,
    int BlockSizePerChannel,
    long DataOffset,
    long DataEnd,
    bool IsDff,
    bool IsLsbFirst)
{
    public TimeSpan Duration => SampleRate > 0
        ? TimeSpan.FromSeconds(SampleCount / (double)SampleRate)
        : TimeSpan.Zero;

    public int BaseFamilyRate => SampleRate % 48000 == 0 && SampleRate % 44100 != 0 ? 48000 : 44100;

    public int DoPPcmSampleRate => SampleRate / 16;

    public string DsdRateLabel => (SampleRate / BaseFamilyRate) switch
    {
        <= 64 => "DSD64",
        128 => "DSD128",
        256 => "DSD256",
        512 => "DSD512",
        var mult => $"DSD{mult}"
    };
}

public static class DsdFileInspector
{
    public static DsdStreamMetadata ReadMetadata(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return ReadMetadata(stream);
    }

    public static DsdStreamMetadata ReadMetadata(Stream stream)
    {
        stream.Position = 0;
        Span<byte> id = stackalloc byte[4];
        stream.ReadExactly(id);
        stream.Position = 0;

        if (id.SequenceEqual("DSD "u8))
        {
            return ReadDsfMetadata(stream);
        }

        if (id.SequenceEqual("FRM8"u8))
        {
            return ReadDffMetadata(stream);
        }

        throw new InvalidOperationException("El archivo DSD no tiene una cabecera DSF o DFF válida.");
    }

    private static DsdStreamMetadata ReadDsfMetadata(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("DSD "u8))
        {
            throw new InvalidOperationException("El archivo DSD no tiene cabecera DSF válida.");
        }

        _ = ReadUInt64LittleEndian(stream);
        _ = ReadUInt64LittleEndian(stream);

        var fmt = ReadDsfChunkHeader(stream);
        if (fmt.Id != "fmt ")
        {
            throw new InvalidOperationException("El archivo DSF no contiene el bloque fmt esperado.");
        }

        _ = ReadUInt32LittleEndian(stream); // format version
        _ = ReadUInt32LittleEndian(stream); // format id (0 = DSD raw)
        _ = ReadUInt32LittleEndian(stream); // channel type
        var channels = checked((int)ReadUInt32LittleEndian(stream));
        var sampleRate = checked((int)ReadUInt32LittleEndian(stream));
        var bitsPerSample = ReadUInt32LittleEndian(stream); // 1 = LSB-first, 8 = MSB-first
        var sampleCount = ReadUInt64LittleEndian(stream);
        var blockSize = checked((int)ReadUInt32LittleEndian(stream));
        _ = ReadUInt32LittleEndian(stream); // reserved

        stream.Position = fmt.HeaderStart + checked((long)fmt.Size);

        while (stream.Position + 12 <= stream.Length)
        {
            var chunk = ReadDsfChunkHeader(stream);
            if (chunk.Id == "data")
            {
                var dataOffset = stream.Position;
                var dataEnd = Math.Min(stream.Length, chunk.HeaderStart + checked((long)chunk.Size));
                if (channels <= 0 || channels > 8 || blockSize <= 0 || sampleRate <= 0)
                {
                    throw new InvalidOperationException("El archivo DSF contiene parámetros DSD inválidos.");
                }

                var maxBitsPerChannel = (ulong)((dataEnd - dataOffset) / channels) * 8UL;
                var clampedSamples = sampleCount > 0 ? Math.Min(sampleCount, maxBitsPerChannel) : maxBitsPerChannel;
                var isLsbFirst = bitsPerSample != 8;
                return new DsdStreamMetadata(channels, sampleRate, clampedSamples, blockSize, dataOffset, dataEnd, IsDff: false, IsLsbFirst: isLsbFirst);
            }

            if (chunk.Size < 12)
            {
                break;
            }

            stream.Position = chunk.HeaderStart + checked((long)chunk.Size);
        }

        throw new InvalidOperationException("El archivo DSF no contiene bloque de audio data.");
    }

    private static DsdStreamMetadata ReadDffMetadata(Stream stream)
    {
        var form = ReadDffChunkHeader(stream);
        if (form.Id != "FRM8")
        {
            throw new InvalidOperationException("El archivo DFF no contiene cabecera FRM8 válida.");
        }

        Span<byte> formTypeBuffer = stackalloc byte[4];
        stream.ReadExactly(formTypeBuffer);
        var formType = System.Text.Encoding.ASCII.GetString(formTypeBuffer);
        if (formType != "DSD ")
        {
            throw new InvalidOperationException("El archivo DFF no es DSDIFF DSD sin comprimir.");
        }

        var formEnd = Math.Min(stream.Length, form.HeaderStart + checked((long)form.Size) + 12);
        var channels = 0;
        var sampleRate = 0;
        var dataOffset = 0L;
        var dataEnd = 0L;
        var isCompressedDst = false;

        while (stream.Position + 12 <= formEnd)
        {
            var chunk = ReadDffChunkHeader(stream);
            var chunkDataStart = stream.Position;
            var chunkEnd = Math.Min(stream.Length, chunk.HeaderStart + checked((long)chunk.Size) + 12);

            if (chunk.Id == "PROP")
            {
                ParseDffPropertyChunk(stream, chunkEnd, ref channels, ref sampleRate, ref isCompressedDst);
            }
            else if (chunk.Id == "DSD ")
            {
                dataOffset = chunkDataStart;
                dataEnd = chunkEnd;
            }
            else if (chunk.Id == "DST ")
            {
                isCompressedDst = true;
            }

            stream.Position = AlignEven(chunkEnd);
        }

        if (isCompressedDst)
        {
            throw new InvalidOperationException("Este archivo DFF usa compresión DST (lossless SACD). Requiere MPV o BASS con soporte DST.");
        }

        if (channels <= 0 || channels > 8 || sampleRate <= 0 || dataOffset <= 0 || dataEnd <= dataOffset)
        {
            throw new InvalidOperationException("El archivo DFF no contiene metadatos DSD completos.");
        }

        var dataBytes = dataEnd - dataOffset;
        var sampleCount = checked((ulong)((dataBytes / channels) * 8));
        return new DsdStreamMetadata(channels, sampleRate, sampleCount, BlockSizePerChannel: 4096, dataOffset, dataEnd, IsDff: true, IsLsbFirst: false);
    }

    private static void ParseDffPropertyChunk(
        Stream stream,
        long propertyEnd,
        ref int channels,
        ref int sampleRate,
        ref bool isCompressedDst)
    {
        Span<byte> propertyTypeBuffer = stackalloc byte[4];
        stream.ReadExactly(propertyTypeBuffer);
        if (!propertyTypeBuffer.SequenceEqual("SND "u8))
        {
            return;
        }

        Span<byte> compressionBuffer = stackalloc byte[4];
        while (stream.Position + 12 <= propertyEnd)
        {
            var property = ReadDffChunkHeader(stream);
            var propertyDataStart = stream.Position;
            var propertyDataEnd = Math.Min(propertyEnd, property.HeaderStart + checked((long)property.Size) + 12);

            switch (property.Id)
            {
                case "FS  ":
                    sampleRate = checked((int)ReadUInt32BigEndian(stream));
                    break;
                case "CHNL":
                    channels = ReadUInt16BigEndian(stream);
                    break;
                case "CMPR":
                    stream.ReadExactly(compressionBuffer);
                    isCompressedDst = !compressionBuffer.SequenceEqual("DSD "u8);
                    break;
            }

            stream.Position = AlignEven(Math.Max(propertyDataStart, propertyDataEnd));
        }
    }

    private static DsdChunk ReadDsfChunkHeader(Stream stream)
    {
        var headerStart = stream.Position;
        Span<byte> id = stackalloc byte[4];
        stream.ReadExactly(id);
        var size = ReadUInt64LittleEndian(stream);
        return new DsdChunk(System.Text.Encoding.ASCII.GetString(id), size, headerStart);
    }

    private static DsdChunk ReadDffChunkHeader(Stream stream)
    {
        var headerStart = stream.Position;
        Span<byte> id = stackalloc byte[4];
        stream.ReadExactly(id);
        var size = ReadUInt64BigEndian(stream);
        return new DsdChunk(System.Text.Encoding.ASCII.GetString(id), size, headerStart);
    }

    private static ushort ReadUInt16BigEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[2];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    private static uint ReadUInt32LittleEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    private static uint ReadUInt32BigEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }

    private static ulong ReadUInt64LittleEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
    }

    private static ulong ReadUInt64BigEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    private static long AlignEven(long value) => (value & 1) == 0 ? value : value + 1;

    private sealed record DsdChunk(string Id, ulong Size, long HeaderStart);
}

internal sealed class DsdByteReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly byte[] _blockBuffer;
    private long _totalBytesPerChannel;
    private long _currentByteIndex;
    private int _bytesAvailableInBlock;
    private int _byteIndexInBlock;
    private bool _hasBlock;

    public DsdByteReader(string filePath)
    {
        _stream = File.OpenRead(filePath);
        Metadata = DsdFileInspector.ReadMetadata(_stream);
        _totalBytesPerChannel = (long)(Metadata.SampleCount / 8UL);
        _blockBuffer = new byte[Metadata.BlockSizePerChannel * Metadata.Channels];
        SeekByte(0);
    }

    public DsdStreamMetadata Metadata { get; }

    public long CurrentByteIndex => _currentByteIndex;

    public long TotalBytesPerChannel => _totalBytesPerChannel;

    public void SeekByte(long targetByteIndex)
    {
        var clamped = Math.Clamp(targetByteIndex, 0, _totalBytesPerChannel);
        var blockIndex = clamped / Metadata.BlockSizePerChannel;
        var offsetInBlock = (int)(clamped % Metadata.BlockSizePerChannel);
        _currentByteIndex = clamped;

        var filePos = Metadata.DataOffset + (blockIndex * Metadata.BlockSizePerChannel * Metadata.Channels);
        if (filePos >= Metadata.DataEnd || clamped >= _totalBytesPerChannel)
        {
            _stream.Position = Metadata.DataEnd;
            _hasBlock = false;
            _bytesAvailableInBlock = 0;
            _byteIndexInBlock = 0;
            return;
        }

        _stream.Position = filePos;
        ReadNextBlock(offsetInBlock);
    }

    public bool ReadNextFrameBytes(Span<byte> channelBytes)
    {
        if (_currentByteIndex >= _totalBytesPerChannel)
        {
            return false;
        }

        if (!_hasBlock || _byteIndexInBlock >= _bytesAvailableInBlock)
        {
            if (!ReadNextBlock(0))
            {
                return false;
            }
        }

        var channels = Metadata.Channels;
        if (Metadata.IsDff)
        {
            var baseOffset = _byteIndexInBlock * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                channelBytes[ch] = _blockBuffer[baseOffset + ch];
            }
        }
        else
        {
            for (var ch = 0; ch < channels; ch++)
            {
                channelBytes[ch] = _blockBuffer[(ch * Metadata.BlockSizePerChannel) + _byteIndexInBlock];
            }
        }

        _byteIndexInBlock++;
        _currentByteIndex++;
        return true;
    }

    private bool ReadNextBlock(int startByteInBlock)
    {
        if (_stream.Position >= Metadata.DataEnd || _currentByteIndex >= _totalBytesPerChannel)
        {
            _hasBlock = false;
            return false;
        }

        var remainingInFile = (int)Math.Min(_blockBuffer.Length, Metadata.DataEnd - _stream.Position);
        var read = _stream.Read(_blockBuffer, 0, remainingInFile);
        if (read <= 0)
        {
            _hasBlock = false;
            return false;
        }

        if (read < _blockBuffer.Length)
        {
            Array.Clear(_blockBuffer, read, _blockBuffer.Length - read);
        }

        var bytesPerChannelInBlock = Metadata.IsDff
            ? read / Metadata.Channels
            : Math.Min(Metadata.BlockSizePerChannel, read / Metadata.Channels);

        var remainingLogical = _totalBytesPerChannel - (_currentByteIndex - startByteInBlock);
        _bytesAvailableInBlock = (int)Math.Min(bytesPerChannelInBlock, Math.Max(remainingLogical, 0));
        _byteIndexInBlock = Math.Clamp(startByteInBlock, 0, Math.Max(_bytesAvailableInBlock, 0));
        _hasBlock = _byteIndexInBlock < _bytesAvailableInBlock;
        return _hasBlock;
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}

/// <summary>
/// High-precision multistage linear-phase FIR DSD-to-PCM decoder modeled after foobar2000's foo_input_sacd.
/// Stage 1 uses an 8-bit LUT Blackman-Harris FIR from DSD64..DSD512 down to 352.8/384 kHz.
/// Stage 2 cascades clean 2x FIR decimators down to 176.4/88.2/44.1 kHz (or 192/96/48 kHz) with ultrasonic filtering.
/// </summary>
public sealed class DsdPcmSampleProvider : ISampleProvider, IDisposable
{
    private readonly DsdByteReader _reader;
    private readonly int _outputChannels;
    private readonly int _stage1StepBytes;
    private readonly int _stage1FilterBytes;
    private readonly double[][] _stage1Lut;
    private readonly byte[][] _stage1History;
    private readonly int[] _stage1WritePos;
    private readonly List<DecimateBy2Stage> _decimationStages = [];
    private readonly BiquadLowPass[] _ultrasonicFilters;
    private readonly double _gainLinear;
    private readonly object _syncLock = new();
    private long _currentOutputFrame;
    private long _totalOutputFrames;

    public DsdPcmSampleProvider(string filePath, int requestedSampleRate = 88200, double gainDb = 6.0)
    {
        _reader = new DsdByteReader(filePath);
        Metadata = _reader.Metadata;

        var baseFamily = Metadata.BaseFamilyRate;
        var intermediateRate = baseFamily * 8; // 352800 Hz (44.1k family) or 384000 Hz (48k family)
        if (Metadata.SampleRate < intermediateRate || Metadata.SampleRate % intermediateRate != 0)
        {
            _reader.Dispose();
            throw new InvalidOperationException($"Frecuencia DSD no compatible con decodificador PCM FIR: {Metadata.SampleRate} Hz.");
        }

        var targetRate = ResolveTargetSampleRate(requestedSampleRate, baseFamily);
        OutputSampleRate = targetRate;
        _outputChannels = Metadata.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(OutputSampleRate, _outputChannels);
        _gainLinear = Math.Pow(10.0, Math.Clamp(gainDb, -12.0, 12.0) / 20.0);

        // Stage 1: DSD -> 8x base rate (352.8 kHz or 384 kHz)
        var stage1RatioBits = Metadata.SampleRate / intermediateRate;
        _stage1StepBytes = Math.Max(1, stage1RatioBits / 8);
        _stage1FilterBytes = 20 * _stage1StepBytes; // 160 taps for DSD64, 320 for DSD128, 640 for DSD256, 1280 for DSD512
        var stage1Taps = _stage1FilterBytes * 8;
        var stage1Cutoff = 135000.0 * (baseFamily / 44100.0);
        var stage1Coeffs = BuildBlackmanHarrisLowPass(stage1Taps, Metadata.SampleRate, stage1Cutoff);
        _stage1Lut = BuildByteLookupTables(stage1Coeffs, _stage1FilterBytes, Metadata.IsLsbFirst);

        _stage1History = new byte[_outputChannels][];
        _stage1WritePos = new int[_outputChannels];
        for (var ch = 0; ch < _outputChannels; ch++)
        {
            _stage1History[ch] = new byte[_stage1FilterBytes];
        }

        // Stage 2: Cascade 2x FIR decimators down to requested output sample rate
        var rate = intermediateRate;
        while (rate > OutputSampleRate)
        {
            var nextRate = rate / 2;
            var taps = nextRate <= baseFamily ? 95 : (nextRate <= baseFamily * 2 ? 63 : 31);
            var cutoff = nextRate <= baseFamily
                ? baseFamily * 0.455
                : (nextRate <= baseFamily * 2 ? 32000.0 : 52000.0);
            _decimationStages.Add(new DecimateBy2Stage(_outputChannels, taps, rate, cutoff));
            rate = nextRate;
        }

        // Gentle Scarlet-Book 50 kHz ultrasonic filter for high-rate PCM outputs (176.4 / 352.8 kHz)
        _ultrasonicFilters = new BiquadLowPass[_outputChannels];
        var ultrasonicCutoff = Math.Min(50000.0, OutputSampleRate * 0.44);
        for (var ch = 0; ch < _outputChannels; ch++)
        {
            _ultrasonicFilters[ch] = new BiquadLowPass(OutputSampleRate, ultrasonicCutoff, q: 0.70710678);
        }

        _totalOutputFrames = (long)Math.Round(Metadata.Duration.TotalSeconds * OutputSampleRate);
    }

    public DsdStreamMetadata Metadata { get; }

    public int OutputSampleRate { get; }

    public WaveFormat WaveFormat { get; }

    public TimeSpan Duration => Metadata.Duration;

    public TimeSpan Position => TimeSpan.FromSeconds(_currentOutputFrame / (double)OutputSampleRate);

    public static int ResolveTargetSampleRate(int requestedSampleRate, int baseFamily)
    {
        if (requestedSampleRate <= 0)
        {
            return baseFamily * 2; // Default 88.2 kHz / 96 kHz
        }

        var supported = new[] { baseFamily, baseFamily * 2, baseFamily * 4, baseFamily * 8 };
        return supported.OrderBy(rate => Math.Abs(rate - requestedSampleRate)).First();
    }

    public void Seek(TimeSpan position)
    {
        lock (_syncLock)
        {
            var clampedSeconds = Math.Clamp(position.TotalSeconds, 0.0, Duration.TotalSeconds);
            var targetOutputFrame = (long)Math.Round(clampedSeconds * OutputSampleRate);
            var decimationFactor = 1L << _decimationStages.Count;
            var targetIntermediateFrame = targetOutputFrame * decimationFactor;
            var targetByte = targetIntermediateFrame * _stage1StepBytes;

            _reader.SeekByte(targetByte);
            _currentOutputFrame = targetOutputFrame;

            for (var ch = 0; ch < _outputChannels; ch++)
            {
                Array.Clear(_stage1History[ch]);
                _stage1WritePos[ch] = 0;
                _ultrasonicFilters[ch].Reset();
            }

            foreach (var stage in _decimationStages)
            {
                stage.Reset();
            }
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_syncLock)
        {
            var framesRequested = count / _outputChannels;
            var framesWritten = 0;
            Span<double> frameSamples = stackalloc double[_outputChannels];

            while (framesWritten < framesRequested && _currentOutputFrame < _totalOutputFrames)
            {
                if (!ProduceOutputFrame(frameSamples))
                {
                    break;
                }

                var outBase = offset + (framesWritten * _outputChannels);
                for (var ch = 0; ch < _outputChannels; ch++)
                {
                    var sample = frameSamples[ch];
                    if (OutputSampleRate >= 176400)
                    {
                        sample = _ultrasonicFilters[ch].Process(sample);
                    }

                    sample *= _gainLinear;
                    sample = SoftLimit(sample);
                    buffer[outBase + ch] = (float)sample;
                }

                framesWritten++;
                _currentOutputFrame++;
            }

            return framesWritten * _outputChannels;
        }
    }

    private bool ProduceOutputFrame(Span<double> output)
    {
        if (_decimationStages.Count == 0)
        {
            return ProduceStage1Frame(output);
        }

        return ProduceDecimatedFrame(_decimationStages.Count - 1, output);
    }

    private bool ProduceDecimatedFrame(int stageIndex, Span<double> output)
    {
        Span<double> inputA = stackalloc double[_outputChannels];
        Span<double> inputB = stackalloc double[_outputChannels];

        if (stageIndex == 0)
        {
            if (!ProduceStage1Frame(inputA) || !ProduceStage1Frame(inputB))
            {
                return false;
            }
        }
        else
        {
            if (!ProduceDecimatedFrame(stageIndex - 1, inputA) || !ProduceDecimatedFrame(stageIndex - 1, inputB))
            {
                return false;
            }
        }

        _decimationStages[stageIndex].ProcessPair(inputA, inputB, output);
        return true;
    }

    private bool ProduceStage1Frame(Span<double> output)
    {
        Span<byte> channelBytes = stackalloc byte[_outputChannels];
        for (var step = 0; step < _stage1StepBytes; step++)
        {
            if (!_reader.ReadNextFrameBytes(channelBytes))
            {
                return false;
            }

            for (var ch = 0; ch < _outputChannels; ch++)
            {
                var pos = _stage1WritePos[ch];
                _stage1History[ch][pos] = channelBytes[ch];
                pos++;
                if (pos >= _stage1FilterBytes)
                {
                    pos = 0;
                }

                _stage1WritePos[ch] = pos;
            }
        }

        for (var ch = 0; ch < _outputChannels; ch++)
        {
            var history = _stage1History[ch];
            var idx = _stage1WritePos[ch]; // oldest byte in ring buffer
            var sum = 0.0;
            for (var b = 0; b < _stage1FilterBytes; b++)
            {
                sum += _stage1Lut[b][history[idx]];
                idx++;
                if (idx >= _stage1FilterBytes)
                {
                    idx = 0;
                }
            }

            output[ch] = sum;
        }

        return true;
    }

    private static double SoftLimit(double sample)
    {
        const double threshold = 0.94;
        var abs = Math.Abs(sample);
        if (abs <= threshold)
        {
            return sample;
        }

        var excess = (abs - threshold) / (1.0 - threshold);
        var limited = threshold + ((1.0 - threshold) * Math.Tanh(excess));
        return Math.CopySign(Math.Min(limited, 0.9999), sample);
    }

    private static double[] BuildBlackmanHarrisLowPass(int taps, double sampleRate, double cutoffHz)
    {
        var coeffs = new double[taps];
        var normalizedCutoff = Math.Clamp(cutoffHz / sampleRate, 0.001, 0.49);
        var center = (taps - 1) / 2.0;
        var sum = 0.0;

        for (var i = 0; i < taps; i++)
        {
            var x = i - center;
            var sinc = Math.Abs(x) < 1e-12
                ? 2.0 * normalizedCutoff
                : Math.Sin(2.0 * Math.PI * normalizedCutoff * x) / (Math.PI * x);

            var n = i / (double)(taps - 1);
            var window = 0.35875
                - (0.48829 * Math.Cos(2.0 * Math.PI * n))
                + (0.14128 * Math.Cos(4.0 * Math.PI * n))
                - (0.01168 * Math.Cos(6.0 * Math.PI * n));

            coeffs[i] = sinc * window;
            sum += coeffs[i];
        }

        if (Math.Abs(sum) > 1e-12)
        {
            for (var i = 0; i < taps; i++)
            {
                coeffs[i] /= sum;
            }
        }

        return coeffs;
    }

    private static double[][] BuildByteLookupTables(double[] coeffs, int filterBytes, bool isLsbFirst)
    {
        var tables = new double[filterBytes][];
        for (var b = 0; b < filterBytes; b++)
        {
            var table = new double[256];
            var baseTap = b * 8;
            for (var byteValue = 0; byteValue < 256; byteValue++)
            {
                var acc = 0.0;
                for (var bit = 0; bit < 8; bit++)
                {
                    var isOne = isLsbFirst
                        ? ((byteValue >> bit) & 1) != 0
                        : ((byteValue >> (7 - bit)) & 1) != 0;
                    acc += isOne ? coeffs[baseTap + bit] : -coeffs[baseTap + bit];
                }

                table[byteValue] = acc;
            }

            tables[b] = table;
        }

        return tables;
    }

    public void Dispose()
    {
        _reader.Dispose();
    }

    private sealed class DecimateBy2Stage
    {
        private readonly double[] _coeffs;
        private readonly double[][] _history;
        private readonly int[] _writePos;
        private readonly int _taps;

        public DecimateBy2Stage(int channels, int taps, double inputSampleRate, double cutoffHz)
        {
            _taps = taps;
            _coeffs = BuildBlackmanHarrisLowPass(taps, inputSampleRate, cutoffHz);
            _history = new double[channels][];
            _writePos = new int[channels];
            for (var ch = 0; ch < channels; ch++)
            {
                _history[ch] = new double[taps];
            }
        }

        public void Reset()
        {
            for (var ch = 0; ch < _history.Length; ch++)
            {
                Array.Clear(_history[ch]);
                _writePos[ch] = 0;
            }
        }

        public void ProcessPair(ReadOnlySpan<double> sampleA, ReadOnlySpan<double> sampleB, Span<double> output)
        {
            for (var ch = 0; ch < _history.Length; ch++)
            {
                var hist = _history[ch];
                var pos = _writePos[ch];

                hist[pos] = sampleA[ch];
                pos = (pos + 1) % _taps;

                hist[pos] = sampleB[ch];
                pos = (pos + 1) % _taps;
                _writePos[ch] = pos;

                var sum = 0.0;
                var idx = pos;
                for (var k = 0; k < _taps; k++)
                {
                    sum += hist[idx] * _coeffs[k];
                    idx++;
                    if (idx >= _taps)
                    {
                        idx = 0;
                    }
                }

                output[ch] = sum;
            }
        }
    }

    private sealed class BiquadLowPass
    {
        private readonly double _b0;
        private readonly double _b1;
        private readonly double _b2;
        private readonly double _a1;
        private readonly double _a2;
        private double _z1;
        private double _z2;

        public BiquadLowPass(double sampleRate, double cutoffHz, double q)
        {
            var w0 = 2.0 * Math.PI * Math.Clamp(cutoffHz, 1000.0, sampleRate * 0.45) / sampleRate;
            var cosW0 = Math.Cos(w0);
            var alpha = Math.Sin(w0) / (2.0 * q);
            var a0 = 1.0 + alpha;

            _b0 = ((1.0 - cosW0) * 0.5) / a0;
            _b1 = (1.0 - cosW0) / a0;
            _b2 = ((1.0 - cosW0) * 0.5) / a0;
            _a1 = (-2.0 * cosW0) / a0;
            _a2 = (1.0 - alpha) / a0;
        }

        public double Process(double input)
        {
            var output = (_b0 * input) + _z1;
            _z1 = (_b1 * input) - (_a1 * output) + _z2;
            _z2 = (_b2 * input) - (_a2 * output);
            return output;
        }

        public void Reset()
        {
            _z1 = 0;
            _z2 = 0;
        }
    }
}

/// <summary>
/// DoP v1.1 (DSD over PCM) wave provider supporting both 24-bit packed and 24-in-32 PCM Extensible
/// containers (required by Thesycon/FiiO USB Audio 2.0 drivers) for bit-perfect DSD over WASAPI Exclusive.
/// Never apply software volume or DSP to this stream; markers 0x05/0xFA must reach the DAC untouched.
/// </summary>
public sealed class DsdDoPWaveProvider : IWaveProvider, IDisposable
{
    private static readonly byte[] BitReverseTable = BuildBitReverseTable();
    private readonly DsdByteReader _reader;
    private readonly object _syncLock = new();
    private readonly Action<double>? _levelSink;
    private readonly bool _use32BitContainer;
    private long _currentDoPFrame;
    private readonly long _totalDoPFrames;
    private bool _markerToggle;

    public DsdDoPWaveProvider(string filePath, WaveFormat? targetWaveFormat = null, Action<double>? levelSink = null)
    {
        _reader = new DsdByteReader(filePath);
        Metadata = _reader.Metadata;
        _levelSink = levelSink;
        WaveFormat = targetWaveFormat ?? new WaveFormat(Metadata.DoPPcmSampleRate, 24, Metadata.Channels);
        _use32BitContainer = WaveFormat.BitsPerSample == 32;
        _totalDoPFrames = _reader.TotalBytesPerChannel / 2;
    }

    public DsdStreamMetadata Metadata { get; }

    public WaveFormat WaveFormat { get; }

    public TimeSpan Duration => Metadata.Duration;

    public TimeSpan Position => TimeSpan.FromSeconds(_currentDoPFrame / (double)Metadata.DoPPcmSampleRate);

    public void Seek(TimeSpan position)
    {
        lock (_syncLock)
        {
            var clamped = Math.Clamp(position.TotalSeconds, 0.0, Duration.TotalSeconds);
            var targetDoPFrame = (long)Math.Round(clamped * Metadata.DoPPcmSampleRate);
            _reader.SeekByte(targetDoPFrame * 2);
            _currentDoPFrame = targetDoPFrame;
            _markerToggle = (targetDoPFrame & 1) != 0;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        lock (_syncLock)
        {
            var channels = Metadata.Channels;
            var bytesPerSample = _use32BitContainer ? 4 : 3;
            var bytesPerFrame = channels * bytesPerSample;
            var maxFrames = count / bytesPerFrame;
            var framesWritten = 0;
            Span<byte> firstByte = stackalloc byte[channels];
            Span<byte> secondByte = stackalloc byte[channels];
            var energySum = 0.0;
            var energyCount = 0;

            while (framesWritten < maxFrames && _currentDoPFrame < _totalDoPFrames)
            {
                if (!_reader.ReadNextFrameBytes(firstByte) || !_reader.ReadNextFrameBytes(secondByte))
                {
                    break;
                }

                var marker = _markerToggle ? (byte)0xFA : (byte)0x05;
                _markerToggle = !_markerToggle;

                var frameOffset = offset + (framesWritten * bytesPerFrame);
                for (var ch = 0; ch < channels; ch++)
                {
                    var msbData = Metadata.IsLsbFirst ? BitReverseTable[firstByte[ch]] : firstByte[ch];
                    var lsbData = Metadata.IsLsbFirst ? BitReverseTable[secondByte[ch]] : secondByte[ch];

                    var sampleOffset = frameOffset + (ch * bytesPerSample);
                    if (_use32BitContainer)
                    {
                        buffer[sampleOffset] = 0x00;
                        buffer[sampleOffset + 1] = lsbData;
                        buffer[sampleOffset + 2] = msbData;
                        buffer[sampleOffset + 3] = marker;
                    }
                    else
                    {
                        buffer[sampleOffset] = lsbData;
                        buffer[sampleOffset + 1] = msbData;
                        buffer[sampleOffset + 2] = marker;
                    }

                    // Estimate level for VU meter without altering the DoP bitstream
                    var ones = System.Numerics.BitOperations.PopCount(msbData) + System.Numerics.BitOperations.PopCount(lsbData);
                    var centered = ((ones / 16.0) * 2.0) - 1.0;
                    energySum += centered * centered;
                    energyCount++;
                }

                framesWritten++;
                _currentDoPFrame++;
            }

            if (energyCount > 0)
            {
                _levelSink?.Invoke(Math.Clamp(Math.Sqrt(energySum / energyCount) * 1.4, 0.0, 1.0));
            }

            return framesWritten * bytesPerFrame;
        }
    }

    private static byte[] BuildBitReverseTable()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            byte reversed = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                if (((i >> bit) & 1) != 0)
                {
                    reversed |= (byte)(1 << (7 - bit));
                }
            }

            table[i] = reversed;
        }

        return table;
    }

    public void Dispose()
    {
        _reader.Dispose();
    }
}
