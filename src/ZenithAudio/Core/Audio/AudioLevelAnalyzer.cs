using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ZenithAudio.Core.Audio;

public sealed class AudioLevelAnalyzer : IDisposable
{
    private MediaFoundationReader? _reader;
    private ISampleProvider? _samples;
    private float[] _buffer = new float[4096];
    private string? _filePath;

    public bool IsReady => _reader is not null && _samples is not null;

    public bool Open(string filePath)
    {
        DisposeReader();

        try
        {
            _reader = new MediaFoundationReader(filePath);
            _samples = _reader.ToSampleProvider();
            _filePath = filePath;
            return true;
        }
        catch
        {
            DisposeReader();
            return false;
        }
    }

    public double ReadLevel(TimeSpan position)
    {
        if (_reader is null || _samples is null)
        {
            return 0;
        }

        try
        {
            var delta = (_reader.CurrentTime - position).Duration();
            if (delta > TimeSpan.FromMilliseconds(120))
            {
                _reader.CurrentTime = position;
            }

            var read = _samples.Read(_buffer, 0, _buffer.Length);
            if (read <= 0)
            {
                return 0;
            }

            var sumSquares = 0.0;
            for (var i = 0; i < read; i++)
            {
                var sample = Math.Clamp(_buffer[i], -1f, 1f);
                sumSquares += sample * sample;
            }

            return Math.Sqrt(sumSquares / read);
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        DisposeReader();
        GC.SuppressFinalize(this);
    }

    private void DisposeReader()
    {
        _reader?.Dispose();
        _reader = null;
        _samples = null;
        _filePath = null;
    }
}
