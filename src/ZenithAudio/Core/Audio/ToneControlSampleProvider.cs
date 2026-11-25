using NAudio.Wave;

namespace ZenithAudio.Core.Audio;

/// <summary>
/// Real-time 3-band biquad tone control (Low-Shelf SubBass, Peaking Presence, High-Shelf Air),
/// Preamp, Volume, thread-safe de-clicked seeking, and live RMS VU meter tap.
/// True bit-perfect pass-through when bypassed at 100% volume and no seek ramp is active.
/// </summary>
public sealed class ToneControlSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly BufferedAudioSampleProvider? _bufferedSource;
    private readonly ToneControlSettings? _settings;
    private readonly Action<double>? _levelSink;
    private readonly object _streamLock = new();
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly BiquadFilter[] _subBassFilters;
    private readonly BiquadFilter[] _presenceFilters;
    private readonly BiquadFilter[] _airFilters;
    private readonly double[] _lastOutputSamples;
    private readonly double[] _fadeOutStartSamples;
    private readonly int _fadeOutTotalFrames;
    private readonly int _fadeInTotalFrames;
    private int _fadeOutFramesRemaining;
    private int _fadeInFramesRemaining;
    private double _lastSubBassDb = double.NaN;
    private double _lastPresenceDb = double.NaN;
    private double _lastAirDb = double.NaN;
    private float _volume = 1.0f;

    public ToneControlSampleProvider(
        ISampleProvider source,
        ToneControlSettings? settings,
        Action<double>? levelSink = null,
        float initialVolume = 1.0f)
    {
        _source = source;
        _bufferedSource = source as BufferedAudioSampleProvider;
        _settings = settings;
        _levelSink = levelSink;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;
        _volume = Math.Clamp(initialVolume, 0.0f, 1.0f);

        _lastOutputSamples = new double[_channels];
        _fadeOutStartSamples = new double[_channels];
        _fadeOutTotalFrames = Math.Clamp((int)Math.Round(_sampleRate * 0.005), 32, 4096); // ~5 ms ramp to zero
        _fadeInTotalFrames = Math.Clamp((int)Math.Round(_sampleRate * 0.014), 64, 16384); // ~14 ms raised-cosine ramp-in

        _subBassFilters = new BiquadFilter[_channels];
        _presenceFilters = new BiquadFilter[_channels];
        _airFilters = new BiquadFilter[_channels];
        for (var ch = 0; ch < _channels; ch++)
        {
            _subBassFilters[ch] = new BiquadFilter();
            _presenceFilters[ch] = new BiquadFilter();
            _airFilters[ch] = new BiquadFilter();
        }
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float Volume
    {
        get => Volatile.Read(ref _volume);
        set => Volatile.Write(ref _volume, Math.Clamp(value, 0.0f, 1.0f));
    }

    public bool IsProcessingActive => _settings?.IsActive == true;

    /// <summary>
    /// Executes a seek serialized against Read(), clears biquad filter delay lines,
    /// and arms a short zero-crossing de-click cross-ramp (~23 ms total) so seeking
    /// never produces an electrical pop/thump on DACs or headphones.
    /// </summary>
    public void ExecuteSeekSafely(Action seekAction)
    {
        lock (_streamLock)
        {
            seekAction();

            var hasResidualSignal = false;
            for (var ch = 0; ch < _channels; ch++)
            {
                _subBassFilters[ch].Reset();
                _presenceFilters[ch].Reset();
                _airFilters[ch].Reset();
                _fadeOutStartSamples[ch] = _lastOutputSamples[ch];
                if (Math.Abs(_lastOutputSamples[ch]) > 0.00025)
                {
                    hasResidualSignal = true;
                }
            }

            _fadeOutFramesRemaining = hasResidualSignal ? _fadeOutTotalFrames : 0;
            _fadeInFramesRemaining = _fadeInTotalFrames;
        }
    }

    /// <summary>
    /// Arms a brief smooth ramp-in when resuming from pause to prevent resume pops.
    /// </summary>
    public void ArmResumeRamp()
    {
        lock (_streamLock)
        {
            Array.Clear(_lastOutputSamples);
            _fadeOutFramesRemaining = 0;
            _fadeInFramesRemaining = Math.Max(64, _fadeInTotalFrames / 2);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_streamLock)
        {
            var totalFramesRequested = count / _channels;
            if (totalFramesRequested <= 0)
            {
                return 0;
            }

            var writeOffset = offset;
            var framesProduced = 0;

            // 1. Smoothly ramp any pre-seek residual DC/sample level down to 0.0 (5 ms)
            if (_fadeOutFramesRemaining > 0)
            {
                var rampFrames = Math.Min(totalFramesRequested, _fadeOutFramesRemaining);
                for (var f = 0; f < rampFrames; f++)
                {
                    var progress = (_fadeOutTotalFrames - _fadeOutFramesRemaining + 1) / (double)_fadeOutTotalFrames;
                    var gain = 0.5 * (1.0 + Math.Cos(Math.PI * progress)); // Smooth raised-cosine 1 -> 0
                    var baseIndex = writeOffset + (f * _channels);
                    for (var ch = 0; ch < _channels; ch++)
                    {
                        var sample = _fadeOutStartSamples[ch] * gain;
                        buffer[baseIndex + ch] = (float)sample;
                        _lastOutputSamples[ch] = sample;
                    }

                    _fadeOutFramesRemaining--;
                }

                writeOffset += rampFrames * _channels;
                framesProduced += rampFrames;

                if (_fadeOutFramesRemaining == 0)
                {
                    Array.Clear(_lastOutputSamples);
                }

                if (framesProduced >= totalFramesRequested)
                {
                    return framesProduced * _channels;
                }
            }

            // 2. Read new audio samples from the underlying decoder
            var remainingSamples = (totalFramesRequested - framesProduced) * _channels;
            var seekTransitionBeforeRead = _bufferedSource?.IsSeekTransitionActive == true;
            var read = _source.Read(buffer, writeOffset, remainingSamples);
            if (read <= 0)
            {
                return framesProduced * _channels;
            }

            // If buffered producer is still completing disk seek (emitted clean silence),
            // preserve _fadeInFramesRemaining until actual post-seek audio arrives!
            if (seekTransitionBeforeRead && _bufferedSource?.IsSeekTransitionActive == true)
            {
                Array.Clear(_lastOutputSamples);
                return (framesProduced * _channels) + read;
            }

            var settings = _settings;
            var eqActive = settings is not null && settings.IsActive;
            var volume = Volume;
            var applyVolume = Math.Abs(volume - 1.0f) > 0.0001f;
            var fadingIn = _fadeInFramesRemaining > 0;

            if (eqActive && settings is not null)
            {
                EnsureCoefficients(settings.SubBassDb, settings.PresenceDb, settings.AirDb);
            }

            var preampLinear = eqActive && settings is not null
                ? (float)Math.Pow(10.0, Math.Clamp(settings.PreampDb, -18.0, 12.0) / 20.0)
                : 1.0f;

            var sumSquares = 0.0;
            var frames = read / _channels;

            if (!eqActive && !applyVolume && !fadingIn)
            {
                // Bit-perfect transparent pass-through: do not modify buffer samples at all
                if (frames > 0)
                {
                    var lastBase = writeOffset + ((frames - 1) * _channels);
                    for (var ch = 0; ch < _channels; ch++)
                    {
                        _lastOutputSamples[ch] = buffer[lastBase + ch];
                    }
                }

                if (_levelSink is not null)
                {
                    for (var i = 0; i < read; i++)
                    {
                        double sample = buffer[writeOffset + i];
                        sumSquares += sample * sample;
                    }

                    _levelSink.Invoke(Math.Clamp(Math.Sqrt(sumSquares / read), 0.0, 1.0));
                }

                return (framesProduced * _channels) + read;
            }

            for (var frame = 0; frame < frames; frame++)
            {
                var fadeGain = 1.0;
                if (_fadeInFramesRemaining > 0)
                {
                    var progress = (_fadeInTotalFrames - _fadeInFramesRemaining + 1) / (double)_fadeInTotalFrames;
                    fadeGain = 0.5 * (1.0 - Math.Cos(Math.PI * progress)); // Smooth raised-cosine 0 -> 1
                    _fadeInFramesRemaining--;
                }

                var baseIndex = writeOffset + (frame * _channels);
                for (var ch = 0; ch < _channels; ch++)
                {
                    double sample = buffer[baseIndex + ch];

                    if (eqActive)
                    {
                        if (Math.Abs(_lastSubBassDb) > 0.01)
                        {
                            sample = _subBassFilters[ch].Process(sample);
                        }

                        if (Math.Abs(_lastPresenceDb) > 0.01)
                        {
                            sample = _presenceFilters[ch].Process(sample);
                        }

                        if (Math.Abs(_lastAirDb) > 0.01)
                        {
                            sample = _airFilters[ch].Process(sample);
                        }

                        sample *= preampLinear;
                    }

                    if (applyVolume)
                    {
                        sample *= volume;
                    }

                    if (fadeGain < 1.0)
                    {
                        sample *= fadeGain;
                    }

                    if (sample > 1.0)
                    {
                        sample = 1.0;
                    }
                    else if (sample < -1.0)
                    {
                        sample = -1.0;
                    }

                    buffer[baseIndex + ch] = (float)sample;
                    _lastOutputSamples[ch] = sample;
                    sumSquares += sample * sample;
                }
            }

            if (_levelSink is not null && read > 0)
            {
                _levelSink.Invoke(Math.Clamp(Math.Sqrt(sumSquares / read), 0.0, 1.0));
            }

            return (framesProduced * _channels) + read;
        }
    }

    private void EnsureCoefficients(double subBassDb, double presenceDb, double airDb)
    {
        if (Math.Abs(subBassDb - _lastSubBassDb) > 0.01)
        {
            _lastSubBassDb = subBassDb;
            for (var ch = 0; ch < _channels; ch++)
            {
                _subBassFilters[ch].SetLowShelf(_sampleRate, 85.0, 0.70710678, subBassDb);
            }
        }

        if (Math.Abs(presenceDb - _lastPresenceDb) > 0.01)
        {
            _lastPresenceDb = presenceDb;
            for (var ch = 0; ch < _channels; ch++)
            {
                _presenceFilters[ch].SetPeakingEq(_sampleRate, 2800.0, 0.9, presenceDb);
            }
        }

        if (Math.Abs(airDb - _lastAirDb) > 0.01)
        {
            _lastAirDb = airDb;
            var airFreq = Math.Min(11000.0, _sampleRate * 0.42);
            for (var ch = 0; ch < _channels; ch++)
            {
                _airFilters[ch].SetHighShelf(_sampleRate, airFreq, 0.70710678, airDb);
            }
        }
    }

    private sealed class BiquadFilter
    {
        private double _b0 = 1.0;
        private double _b1;
        private double _b2;
        private double _a1;
        private double _a2;
        private double _z1;
        private double _z2;

        public void SetLowShelf(double sampleRate, double frequency, double slope, double gainDb)
        {
            var a = Math.Pow(10.0, gainDb / 40.0);
            var w0 = 2.0 * Math.PI * Math.Clamp(frequency, 20.0, sampleRate * 0.45) / sampleRate;
            var cosW0 = Math.Cos(w0);
            var sinW0 = Math.Sin(w0);
            var alpha = sinW0 / 2.0 * Math.Sqrt(((a + (1.0 / a)) * ((1.0 / slope) - 1.0)) + 2.0);
            var twoSqrtAAlpha = 2.0 * Math.Sqrt(a) * alpha;

            var b0 = a * ((a + 1.0) - ((a - 1.0) * cosW0) + twoSqrtAAlpha);
            var b1 = 2.0 * a * ((a - 1.0) - ((a + 1.0) * cosW0));
            var b2 = a * ((a + 1.0) - ((a - 1.0) * cosW0) - twoSqrtAAlpha);
            var a0 = (a + 1.0) + ((a - 1.0) * cosW0) + twoSqrtAAlpha;
            var a1 = -2.0 * ((a - 1.0) + ((a + 1.0) * cosW0));
            var a2 = (a + 1.0) + ((a - 1.0) * cosW0) - twoSqrtAAlpha;

            Apply(b0, b1, b2, a0, a1, a2);
        }

        public void SetHighShelf(double sampleRate, double frequency, double slope, double gainDb)
        {
            var a = Math.Pow(10.0, gainDb / 40.0);
            var w0 = 2.0 * Math.PI * Math.Clamp(frequency, 20.0, sampleRate * 0.45) / sampleRate;
            var cosW0 = Math.Cos(w0);
            var sinW0 = Math.Sin(w0);
            var alpha = sinW0 / 2.0 * Math.Sqrt(((a + (1.0 / a)) * ((1.0 / slope) - 1.0)) + 2.0);
            var twoSqrtAAlpha = 2.0 * Math.Sqrt(a) * alpha;

            var b0 = a * ((a + 1.0) + ((a - 1.0) * cosW0) + twoSqrtAAlpha);
            var b1 = -2.0 * a * ((a - 1.0) + ((a + 1.0) * cosW0));
            var b2 = a * ((a + 1.0) + ((a - 1.0) * cosW0) - twoSqrtAAlpha);
            var a0 = (a + 1.0) - ((a - 1.0) * cosW0) + twoSqrtAAlpha;
            var a1 = 2.0 * ((a - 1.0) - ((a + 1.0) * cosW0));
            var a2 = (a + 1.0) - ((a - 1.0) * cosW0) - twoSqrtAAlpha;

            Apply(b0, b1, b2, a0, a1, a2);
        }

        public void SetPeakingEq(double sampleRate, double frequency, double q, double gainDb)
        {
            var a = Math.Pow(10.0, gainDb / 40.0);
            var w0 = 2.0 * Math.PI * Math.Clamp(frequency, 20.0, sampleRate * 0.45) / sampleRate;
            var cosW0 = Math.Cos(w0);
            var alpha = Math.Sin(w0) / (2.0 * q);

            var b0 = 1.0 + (alpha * a);
            var b1 = -2.0 * cosW0;
            var b2 = 1.0 - (alpha * a);
            var a0 = 1.0 + (alpha / a);
            var a1 = -2.0 * cosW0;
            var a2 = 1.0 - (alpha / a);

            Apply(b0, b1, b2, a0, a1, a2);
        }

        private void Apply(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;
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
            _z1 = 0.0;
            _z2 = 0.0;
        }
    }
}
