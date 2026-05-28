using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Windows.Media.Core;
using Windows.Media.MediaProperties;

namespace ZenithAudio.Core.Audio;

public static class WavPcmStreamSource
{
    private const int TargetBitsPerSample = 16;
    private const int TargetBufferMilliseconds = 120;

    public static MediaSource CreateMediaSource(string filePath, Action<double>? levelSink = null)
    {
        var stream = new WavPcmStream(filePath, levelSink);
        var properties = AudioEncodingProperties.CreatePcm(
            (uint)stream.SampleRate,
            (uint)stream.OutputChannels,
            TargetBitsPerSample);
        var descriptor = new AudioStreamDescriptor(properties);
        var source = new MediaStreamSource(descriptor)
        {
            CanSeek = true,
            Duration = stream.Duration
        };

        source.Starting += (_, args) =>
        {
            var position = args.Request.StartPosition ?? TimeSpan.Zero;
            stream.Seek(position);
            args.Request.SetActualStartPosition(position);
        };

        source.SampleRequested += (_, args) =>
        {
            var timestamp = stream.Position;
            var buffer = stream.ReadPcmBuffer(TargetBufferMilliseconds);
            if (buffer.Length == 0)
            {
                args.Request.Sample = null;
                return;
            }

            args.Request.Sample = MediaStreamSample.CreateFromBuffer(buffer.AsBuffer(), timestamp);
        };

        source.Closed += (_, _) => stream.Dispose();
        return MediaSource.CreateFromMediaStreamSource(source);
    }

    private sealed class WavPcmStream : IDisposable
    {
        private readonly WaveFileReader _reader;
        private readonly ISampleProvider _samples;
        private readonly Action<double>? _levelSink;
        private readonly float[] _floatBuffer;

        public WavPcmStream(string filePath, Action<double>? levelSink)
        {
            _reader = new WaveFileReader(filePath);
            _samples = _reader.ToSampleProvider();
            _levelSink = levelSink;
            SourceChannels = Math.Max(1, _samples.WaveFormat.Channels);
            OutputChannels = SourceChannels == 1 ? 1 : 2;
            SampleRate = _samples.WaveFormat.SampleRate;
            Duration = _reader.TotalTime;
            _floatBuffer = new float[SampleRate * SourceChannels * TargetBufferMilliseconds / 1000];
        }

        public int SourceChannels { get; }

        public int OutputChannels { get; }

        public int SampleRate { get; }

        public TimeSpan Duration { get; }

        public TimeSpan Position => _reader.CurrentTime;

        public byte[] ReadPcmBuffer(int milliseconds)
        {
            var sourceFrames = SampleRate * milliseconds / 1000;
            var sourceSamplesRequested = Math.Min(_floatBuffer.Length, sourceFrames * SourceChannels);
            var sourceSamplesRead = _samples.Read(_floatBuffer, 0, sourceSamplesRequested);
            if (sourceSamplesRead <= 0)
            {
                return [];
            }

            var framesRead = sourceSamplesRead / SourceChannels;
            var output = new byte[framesRead * OutputChannels * sizeof(short)];
            var sumSquares = 0.0;
            var outputSampleCount = 0;

            for (var frame = 0; frame < framesRead; frame++)
            {
                if (OutputChannels == 1)
                {
                    var sample = ClampSample(_floatBuffer[frame]);
                    WriteSample(output, frame, 0, OutputChannels, sample);
                    sumSquares += sample * sample;
                    outputSampleCount++;
                    continue;
                }

                var sourceOffset = frame * SourceChannels;
                var left = ClampSample(_floatBuffer[sourceOffset]);
                var right = SourceChannels > 1 ? ClampSample(_floatBuffer[sourceOffset + 1]) : left;

                if (SourceChannels > 2)
                {
                    var center = ClampSample(_floatBuffer[sourceOffset + 2]);
                    left = Math.Clamp((left * 0.75f) + (center * 0.25f), -1f, 1f);
                    right = Math.Clamp((right * 0.75f) + (center * 0.25f), -1f, 1f);
                }

                WriteSample(output, frame, 0, OutputChannels, left);
                WriteSample(output, frame, 1, OutputChannels, right);
                sumSquares += (left * left) + (right * right);
                outputSampleCount += 2;
            }

            if (outputSampleCount > 0)
            {
                _levelSink?.Invoke(Math.Clamp(Math.Sqrt(sumSquares / outputSampleCount), 0.0, 1.0));
            }

            return output;
        }

        public void Seek(TimeSpan position)
        {
            var clampedTicks = Math.Clamp(position.Ticks, 0, Duration.Ticks);
            _reader.CurrentTime = TimeSpan.FromTicks(clampedTicks);
        }

        public void Dispose()
        {
            _reader.Dispose();
        }

        private static float ClampSample(float value)
        {
            return Math.Clamp(float.IsFinite(value) ? value : 0f, -1f, 1f);
        }

        private static void WriteSample(byte[] output, int frame, int channel, int channels, float sample)
        {
            var value = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
            var offset = ((frame * channels) + channel) * sizeof(short);
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(offset, sizeof(short)), value);
        }
    }
}
