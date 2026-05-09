using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.MediaProperties;

namespace ZenithAudio.Core.Audio;

public static class DsfPcmStreamSource
{
    private const int TargetSampleRate = 88200;
    private const int TargetBitsPerSample = 16;
    private const int TargetBufferMilliseconds = 180;

    public static MediaSource CreateMediaSource(string filePath, ToneControlSettings? toneSettings = null, Action<double>? levelSink = null)
    {
        var stream = new DsfPcmStream(filePath, toneSettings, levelSink);
        var properties = AudioEncodingProperties.CreatePcm(
            TargetSampleRate,
            stream.Channels,
            TargetBitsPerSample);
        var descriptor = new AudioStreamDescriptor(properties);
        var source = new MediaStreamSource(descriptor)
        {
            CanSeek = true,
            Duration = stream.Duration
        };

        source.Starting += (_, args) =>
        {
            if (args.Request.StartPosition is { } startPosition)
            {
                stream.Seek(startPosition);
                args.Request.SetActualStartPosition(startPosition);
            }
            else
            {
                stream.Seek(TimeSpan.Zero);
                args.Request.SetActualStartPosition(TimeSpan.Zero);
            }
        };

        source.SampleRequested += (_, args) =>
        {
            var maxFrames = TargetSampleRate * TargetBufferMilliseconds / 1000;
            var buffer = new byte[maxFrames * stream.Channels * sizeof(short)];
            var timestamp = stream.Position;
            var bytesWritten = stream.ReadPcm(buffer);
            if (bytesWritten <= 0)
            {
                args.Request.Sample = null;
                return;
            }

            if (bytesWritten != buffer.Length)
            {
                Array.Resize(ref buffer, bytesWritten);
            }

            args.Request.Sample = MediaStreamSample.CreateFromBuffer(buffer.AsBuffer(), timestamp);
        };

        source.Closed += (_, _) => stream.Dispose();
        return MediaSource.CreateFromMediaStreamSource(source);
    }

    private sealed class DsfPcmStream : IDisposable
    {
        private readonly FileStream _input;
        private readonly DsfInfo _info;
        private readonly int _ratio;
        private readonly int _blockBytes;
        private readonly int _framesPerBlock;
        private readonly byte[] _sourceBlock;
        private readonly double[] _channelStates;
        private readonly double[] _subBassStates;
        private readonly double[] _presenceStates;
        private readonly double[] _airStates;
        private readonly ToneControlSettings? _toneSettings;
        private readonly Action<double>? _levelSink;
        private long _currentPcmFrame;
        private int _framesAvailableInBlock;
        private int _frameIndexInBlock;
        private bool _hasBlock;

        public DsfPcmStream(string filePath, ToneControlSettings? toneSettings, Action<double>? levelSink)
        {
            _toneSettings = toneSettings;
            _levelSink = levelSink;
            _input = File.OpenRead(filePath);
            _info = ReadDsfInfo(_input);
            if (_info.Channels <= 0 || _info.Channels > 8)
            {
                throw new InvalidOperationException("DSF fallback no pudo detectar canales validos.");
            }

            if (_info.SampleRate <= TargetSampleRate || _info.SampleRate % TargetSampleRate != 0)
            {
                throw new InvalidOperationException("DSF fallback solo soporta DSD compatible con PCM 88.2 kHz.");
            }

            Channels = (uint)_info.Channels;
            _ratio = _info.SampleRate / TargetSampleRate;
            _blockBytes = _info.BlockSizePerChannel * _info.Channels;
            _framesPerBlock = (_info.BlockSizePerChannel * 8) / _ratio;
            _sourceBlock = new byte[_blockBytes];
            _channelStates = new double[_info.Channels];
            _subBassStates = new double[_info.Channels];
            _presenceStates = new double[_info.Channels];
            _airStates = new double[_info.Channels];
            Duration = TimeSpan.FromSeconds(_info.SampleCount / (double)_info.SampleRate);
            Seek(TimeSpan.Zero);
        }

        public uint Channels { get; }

        public TimeSpan Duration { get; }

        public TimeSpan Position => TimeSpan.FromSeconds(_currentPcmFrame / (double)TargetSampleRate);

        public int ReadPcm(byte[] output)
        {
            var frameSize = _info.Channels * sizeof(short);
            var maxFrames = output.Length / frameSize;
            var outputFrame = 0;
            var sumSquares = 0.0;
            var sampleCount = 0;

            while (outputFrame < maxFrames)
            {
                if (!_hasBlock || _frameIndexInBlock >= _framesAvailableInBlock)
                {
                    if (!ReadNextBlock(0))
                    {
                        break;
                    }
                }

                var bitStart = _frameIndexInBlock * _ratio;
                var outputOffset = outputFrame * frameSize;
                for (var channel = 0; channel < _info.Channels; channel++)
                {
                    var channelOffset = channel * _info.BlockSizePerChannel;
                    var ones = CountOnes(_sourceBlock, channelOffset, bitStart, _ratio);
                    var centered = ((ones / (double)_ratio) * 2.0) - 1.0;
                    _channelStates[channel] = (_channelStates[channel] * 0.92) + (centered * 0.08);
                    var shaped = ApplyTone(channel, _channelStates[channel]);
                    sumSquares += shaped * shaped;
                    sampleCount++;
                    var sample = (short)Math.Clamp(shaped * short.MaxValue, short.MinValue, short.MaxValue);
                    BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(outputOffset + (channel * sizeof(short)), sizeof(short)), sample);
                }

                _frameIndexInBlock++;
                _currentPcmFrame++;
                outputFrame++;
            }

            if (sampleCount > 0)
            {
                _levelSink?.Invoke(Math.Clamp(Math.Sqrt(sumSquares / sampleCount), 0.0, 1.0));
            }

            return outputFrame * frameSize;
        }

        public void Seek(TimeSpan position)
        {
            var targetFrame = Math.Clamp((long)(position.TotalSeconds * TargetSampleRate), 0, (long)(Duration.TotalSeconds * TargetSampleRate));
            var blockIndex = targetFrame / _framesPerBlock;
            _frameIndexInBlock = (int)(targetFrame % _framesPerBlock);
            _currentPcmFrame = targetFrame;
            Array.Clear(_channelStates);
            Array.Clear(_subBassStates);
            Array.Clear(_presenceStates);
            Array.Clear(_airStates);

            var sourcePosition = _info.DataOffset + (blockIndex * _blockBytes);
            if (sourcePosition >= _info.DataEnd)
            {
                _input.Position = _info.DataEnd;
                _hasBlock = false;
                return;
            }

            _input.Position = sourcePosition;
            ReadNextBlock(_frameIndexInBlock);
        }

        public void Dispose()
        {
            _input.Dispose();
        }

        private bool ReadNextBlock(int startFrame)
        {
            if (_input.Position >= _info.DataEnd)
            {
                _hasBlock = false;
                return false;
            }

            var remaining = (int)Math.Min(_sourceBlock.Length, _info.DataEnd - _input.Position);
            var read = _input.Read(_sourceBlock, 0, remaining);
            if (read <= 0)
            {
                _hasBlock = false;
                return false;
            }

            if (read < _sourceBlock.Length)
            {
                Array.Clear(_sourceBlock, read, _sourceBlock.Length - read);
            }

            _framesAvailableInBlock = (read / _info.Channels * 8) / _ratio;
            _frameIndexInBlock = Math.Clamp(startFrame, 0, Math.Max(_framesAvailableInBlock - 1, 0));
            _hasBlock = _framesAvailableInBlock > 0;
            return _hasBlock;
        }

        private double ApplyTone(int channel, double sample)
        {
            var settings = _toneSettings;
            if (settings is null || !settings.IsActive)
            {
                return sample;
            }

            _subBassStates[channel] = (_subBassStates[channel] * 0.995) + (sample * 0.005);
            _presenceStates[channel] = (_presenceStates[channel] * 0.94) + (sample * 0.06);
            _airStates[channel] = (_airStates[channel] * 0.72) + (sample * 0.28);

            var sub = _subBassStates[channel];
            var presence = sample - _presenceStates[channel];
            var air = sample - _airStates[channel];

            var shaped = sample;
            shaped += sub * DbToLinearDelta(settings.SubBassDb) * 0.7;
            shaped += presence * DbToLinearDelta(settings.PresenceDb) * 0.45;
            shaped += air * DbToLinearDelta(settings.AirDb) * 0.35;
            shaped *= DbToLinear(settings.PreampDb);

            return Math.Clamp(shaped, -1.0, 1.0);
        }

        private static double DbToLinear(double db)
        {
            return Math.Pow(10.0, db / 20.0);
        }

        private static double DbToLinearDelta(double db)
        {
            return DbToLinear(db) - 1.0;
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

        private static DsfInfo ReadDsfInfo(Stream stream)
        {
            Span<byte> header = stackalloc byte[12];
            stream.ReadExactly(header);
            if (!header[..4].SequenceEqual("DSD "u8))
            {
                throw new InvalidOperationException("El archivo DSD no tiene cabecera DSF valida.");
            }

            _ = ReadUInt64LittleEndian(stream);
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
            var sampleCount = ReadUInt64LittleEndian(stream);
            var blockSize = checked((int)ReadUInt32LittleEndian(stream));
            _ = ReadUInt32LittleEndian(stream);

            stream.Position = fmt.HeaderStart + checked((long)fmt.Size);

            while (stream.Position < stream.Length)
            {
                var chunk = ReadChunkHeader(stream);
                if (chunk.Id == "data")
                {
                    return new DsfInfo(channels, sampleRate, sampleCount, blockSize, stream.Position, chunk.HeaderStart + checked((long)chunk.Size));
                }

                stream.Position = chunk.HeaderStart + checked((long)chunk.Size);
            }

            throw new InvalidOperationException("El archivo DSF no contiene bloque de audio data.");
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
    }

    private sealed record DsfInfo(int Channels, int SampleRate, ulong SampleCount, int BlockSizePerChannel, long DataOffset, long DataEnd);

    private sealed record DsfChunk(string Id, ulong Size, long HeaderStart);
}
