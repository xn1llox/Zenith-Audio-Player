using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.MediaProperties;

namespace ZenithAudio.Core.Audio;

public static class DsfPcmStreamSource
{
    private const int TargetBitsPerSample = 24;
    private const int TargetBufferMilliseconds = 150;

    public static MediaSource CreateMediaSource(
        string filePath,
        ToneControlSettings? toneSettings = null,
        Action<double>? levelSink = null,
        int requestedSampleRate = 88200,
        double gainDb = 6.0)
    {
        var decoder = new DsdPcmSampleProvider(filePath, requestedSampleRate, gainDb);
        var processor = new ToneControlSampleProvider(decoder, toneSettings, levelSink);
        var sampleRate = (uint)decoder.OutputSampleRate;
        var channels = (uint)decoder.WaveFormat.Channels;

        var properties = AudioEncodingProperties.CreatePcm(
            sampleRate,
            channels,
            TargetBitsPerSample);
        var descriptor = new AudioStreamDescriptor(properties);
        var source = new MediaStreamSource(descriptor)
        {
            CanSeek = true,
            Duration = decoder.Duration
        };

        var maxFrames = decoder.OutputSampleRate * TargetBufferMilliseconds / 1000;
        var floatBuffer = new float[maxFrames * decoder.WaveFormat.Channels];

        source.Starting += (_, args) =>
        {
            var startPosition = args.Request.StartPosition ?? TimeSpan.Zero;
            decoder.Seek(startPosition);
            args.Request.SetActualStartPosition(startPosition);
        };

        source.SampleRequested += (_, args) =>
        {
            var timestamp = decoder.Position;
            var samplesRead = processor.Read(floatBuffer, 0, floatBuffer.Length);
            if (samplesRead <= 0)
            {
                args.Request.Sample = null;
                return;
            }

            var output = new byte[samplesRead * 3];
            for (var i = 0; i < samplesRead; i++)
            {
                var clamped = Math.Clamp(floatBuffer[i], -1.0f, 1.0f);
                var int24 = (int)Math.Round(clamped * 8388607.0f);
                var offset = i * 3;
                output[offset] = (byte)(int24 & 0xFF);
                output[offset + 1] = (byte)((int24 >> 8) & 0xFF);
                output[offset + 2] = (byte)((int24 >> 16) & 0xFF);
            }

            args.Request.Sample = MediaStreamSample.CreateFromBuffer(output.AsBuffer(), timestamp);
        };

        source.Closed += (_, _) => decoder.Dispose();
        return MediaSource.CreateFromMediaStreamSource(source);
    }
}
