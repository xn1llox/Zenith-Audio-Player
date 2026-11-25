using NAudio.Wave;

namespace ZenithAudio.Core.Audio;

/// <summary>
/// Foobar2000-style decoupled producer-consumer read-ahead ring buffer.
/// Decodes PCM/DSD-FIR ~450-650 ms ahead on an AboveNormal background thread so the
/// real-time WASAPI/ASIO consumer thread ONLY copies from RAM (never stalls on disk I/O
/// or MediaFoundation seek latency), while coalescing rapid seek requests cleanly.
/// Preserves bit-exact float samples when not seeking.
/// </summary>
public sealed class BufferedAudioSampleProvider : ISampleProvider, IDisposable
{
    private readonly ISampleProvider _source;
    private readonly Action<TimeSpan> _sourceSeek;
    private readonly Func<TimeSpan> _sourceDurationGetter;
    private readonly Func<TimeSpan> _sourcePositionGetter;
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly float[] _ringBuffer;
    private readonly int _capacitySamples;
    private readonly int _chunkSamples;
    private readonly object _lock = new();
    private readonly AutoResetEvent _wakeProducer = new(false);
    private readonly Thread _producerThread;

    private int _readPos;
    private int _writePos;
    private int _bufferedSamples;
    private bool _sourceEof;
    private volatile bool _disposed;

    // Seek coalescing state
    private bool _seekPending;
    private TimeSpan _pendingSeekTarget;
    private long _seekGeneration;
    private int _minPrebufferSamplesAfterSeek;
    private bool _waitingForPostSeekPrebuffer;

    // Consumer-accurate playhead tracking
    private double _consumerPositionSeconds;

    public BufferedAudioSampleProvider(
        ISampleProvider source,
        Action<TimeSpan> sourceSeek,
        Func<TimeSpan> sourcePositionGetter,
        Func<TimeSpan> sourceDurationGetter,
        int readAheadMilliseconds = 550)
    {
        _source = source;
        _sourceSeek = sourceSeek;
        _sourcePositionGetter = sourcePositionGetter;
        _sourceDurationGetter = sourceDurationGetter;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;

        var clampedMs = Math.Clamp(readAheadMilliseconds, 250, 1200);
        var capacityFrames = Math.Max(4096, (int)Math.Round(_sampleRate * (clampedMs / 1000.0)));
        _capacitySamples = capacityFrames * _channels;
        _ringBuffer = new float[_capacitySamples];

        // Decode in crisp ~25 ms chunks for fast seek latency & low lock contention
        var chunkFrames = Math.Clamp((int)Math.Round(_sampleRate * 0.025), 512, 8192);
        _chunkSamples = chunkFrames * _channels;
        _minPrebufferSamplesAfterSeek = Math.Clamp((int)Math.Round(_sampleRate * 0.020), 256, 4096) * _channels;

        _consumerPositionSeconds = Math.Max(0.0, sourcePositionGetter().TotalSeconds);

        _producerThread = new Thread(ProducerLoop)
        {
            Name = "ZenithAudio.ReadAheadProducer",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _producerThread.Start();

        // Pre-warm initial ~40 ms before hardware starts playing so playback starts instantaneously
        var initialWaitDeadline = Environment.TickCount64 + 120;
        while (Environment.TickCount64 < initialWaitDeadline)
        {
            lock (_lock)
            {
                if (_bufferedSamples >= _minPrebufferSamplesAfterSeek || _sourceEof)
                {
                    break;
                }
            }

            Thread.Sleep(2);
        }
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public bool IsSeekTransitionActive
    {
        get
        {
            lock (_lock)
            {
                return _seekPending || _waitingForPostSeekPrebuffer;
            }
        }
    }

    public TimeSpan Duration => _sourceDurationGetter();

    public TimeSpan Position
    {
        get
        {
            lock (_lock)
            {
                if (_seekPending)
                {
                    return _pendingSeekTarget;
                }

                return TimeSpan.FromSeconds(Math.Max(0.0, _consumerPositionSeconds));
            }
        }
    }

    /// <summary>
    /// Enqueues a non-blocking coalesced seek request. Returns immediately (&lt; 0.1 ms)
    /// so neither the UI thread nor the audio thread ever stalls on disk I/O.
    /// </summary>
    public void RequestSeek(TimeSpan target)
    {
        var duration = Duration;
        var clamped = duration > TimeSpan.Zero
            ? TimeSpan.FromSeconds(Math.Clamp(target.TotalSeconds, 0.0, duration.TotalSeconds))
            : TimeSpan.Zero;

        lock (_lock)
        {
            _pendingSeekTarget = clamped;
            _seekPending = true;
            _seekGeneration++;
            _consumerPositionSeconds = clamped.TotalSeconds;
            _readPos = 0;
            _writePos = 0;
            _bufferedSamples = 0;
            _sourceEof = false;
            _waitingForPostSeekPrebuffer = true;
        }

        _wakeProducer.Set();
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var alignedCount = (count / _channels) * _channels;
        if (alignedCount <= 0)
        {
            return 0;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return 0;
            }

            // While background producer is executing a disk seek or warming the first ~20 ms,
            // output clean silence instead of blocking the real-time audio callback.
            if (_seekPending || (_waitingForPostSeekPrebuffer && _bufferedSamples < _minPrebufferSamplesAfterSeek && !_sourceEof))
            {
                Array.Clear(buffer, offset, alignedCount);
                _wakeProducer.Set();
                return alignedCount;
            }

            _waitingForPostSeekPrebuffer = false;

            if (_bufferedSamples == 0)
            {
                if (_sourceEof)
                {
                    return 0;
                }

                // Temporary producer underrun: emit silence rather than abruptly terminating WASAPI/ASIO!
                Array.Clear(buffer, offset, alignedCount);
                _wakeProducer.Set();
                return alignedCount;
            }

            var toCopy = Math.Min(alignedCount, _bufferedSamples);
            toCopy = (toCopy / _channels) * _channels;

            var firstPart = Math.Min(toCopy, _capacitySamples - _readPos);
            Array.Copy(_ringBuffer, _readPos, buffer, offset, firstPart);
            var secondPart = toCopy - firstPart;
            if (secondPart > 0)
            {
                Array.Copy(_ringBuffer, 0, buffer, offset + firstPart, secondPart);
            }

            _readPos = (_readPos + toCopy) % _capacitySamples;
            _bufferedSamples -= toCopy;
            _consumerPositionSeconds += (toCopy / (double)_channels) / _sampleRate;

            if (toCopy < alignedCount && !_sourceEof)
            {
                // Pad remainder of hardware buffer with silence cleanly
                Array.Clear(buffer, offset + toCopy, alignedCount - toCopy);
                toCopy = alignedCount;
            }

            if (_bufferedSamples < (_capacitySamples * 3) / 4)
            {
                _wakeProducer.Set();
            }

            return toCopy;
        }
    }

    private void ProducerLoop()
    {
        var decodeBuffer = new float[_chunkSamples];

        while (!_disposed)
        {
            bool doSeek;
            TimeSpan seekTarget;
            long activeGeneration;
            int freeSamples;

            lock (_lock)
            {
                doSeek = _seekPending;
                seekTarget = _pendingSeekTarget;
                activeGeneration = _seekGeneration;
                freeSamples = _capacitySamples - _bufferedSamples;

                if (doSeek)
                {
                    _seekPending = false;
                    _readPos = 0;
                    _writePos = 0;
                    _bufferedSamples = 0;
                    _sourceEof = false;
                }
            }

            if (doSeek)
            {
                try
                {
                    _sourceSeek(seekTarget);
                }
                catch
                {
                    // Ignore transient seek exceptions on corrupted frames.
                }

                lock (_lock)
                {
                    // Only commit position if no newer seek superseded it while disk seek ran
                    if (_seekGeneration == activeGeneration)
                    {
                        var actualPos = _sourcePositionGetter().TotalSeconds;
                        _consumerPositionSeconds = actualPos > 0 || seekTarget == TimeSpan.Zero
                            ? actualPos
                            : seekTarget.TotalSeconds;
                    }
                    else
                    {
                        continue;
                    }
                }
            }

            long chunkGeneration;
            bool shouldWait;
            lock (_lock)
            {
                if (_disposed)
                {
                    break;
                }

                if (_seekPending)
                {
                    continue;
                }

                freeSamples = _capacitySamples - _bufferedSamples;
                shouldWait = _sourceEof || freeSamples < _chunkSamples;
                chunkGeneration = _seekGeneration;
            }

            if (shouldWait)
            {
                _wakeProducer.WaitOne(12);
                continue;
            }

            int decoded;
            try
            {
                decoded = _source.Read(decodeBuffer, 0, _chunkSamples);
            }
            catch
            {
                decoded = 0;
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    break;
                }

                // Discard decoded samples if a newer seek arrived while decoding!
                if (_seekPending || _seekGeneration != chunkGeneration)
                {
                    continue;
                }

                if (decoded <= 0)
                {
                    _sourceEof = true;
                    continue;
                }

                var alignedDecoded = (decoded / _channels) * _channels;
                var writable = Math.Min(alignedDecoded, _capacitySamples - _bufferedSamples);
                if (writable > 0)
                {
                    var firstPart = Math.Min(writable, _capacitySamples - _writePos);
                    Array.Copy(decodeBuffer, 0, _ringBuffer, _writePos, firstPart);
                    var secondPart = writable - firstPart;
                    if (secondPart > 0)
                    {
                        Array.Copy(decodeBuffer, firstPart, _ringBuffer, 0, secondPart);
                    }

                    _writePos = (_writePos + writable) % _capacitySamples;
                    _bufferedSamples += writable;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wakeProducer.Set();
        try
        {
            _producerThread.Join(300);
        }
        catch
        {
        }

        _wakeProducer.Dispose();
    }
}
