using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ZenithAudio.Core.Audio;

public sealed record WasapiEndpointDriverInfo(
    bool HasDedicatedDacDriver,
    string DriverTag,
    int MixSampleRate);

public sealed record WasapiPlaybackReport(
    string DeviceName,
    bool IsExclusive,
    bool IsEventDriven,
    bool IsDoP,
    bool IsDsdSource,
    bool IsBitPerfect,
    bool IsResampled,
    int OutputSampleRate,
    int OutputBitDepth,
    int OutputChannels,
    string SourceCodec,
    string DecodeLabel,
    string Summary);

public sealed class WasapiPlaybackEngine : IDisposable
{
    private readonly object _syncLock = new();
    private WasapiOut? _wasapiOut;
    private MMDevice? _activeDevice;
    private BufferedAudioSampleProvider? _bufferedSource;
    private IDisposable? _activeSource;
    private Action<TimeSpan>? _seekAction;
    private Func<TimeSpan>? _positionGetter;
    private Func<TimeSpan>? _durationGetter;
    private ToneControlSampleProvider? _toneProcessor;
    private bool _manualStop;
    private bool _isDoPActive;
    private float _volume = 1.0f;

    public event EventHandler? TrackEnded;

    public event EventHandler<double>? LevelChanged;

    public WasapiPlaybackReport? ActiveReport { get; private set; }

    public bool IsActive
    {
        get
        {
            lock (_syncLock)
            {
                return _wasapiOut is not null;
            }
        }
    }

    public bool IsPlaying
    {
        get
        {
            lock (_syncLock)
            {
                return _wasapiOut?.PlaybackState == NAudio.Wave.PlaybackState.Playing;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_syncLock)
            {
                return _wasapiOut?.PlaybackState == NAudio.Wave.PlaybackState.Paused;
            }
        }
    }

    public bool IsDoPActive => _isDoPActive;

    public TimeSpan Position
    {
        get
        {
            lock (_syncLock)
            {
                try
                {
                    return _positionGetter?.Invoke() ?? TimeSpan.Zero;
                }
                catch
                {
                    return TimeSpan.Zero;
                }
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_syncLock)
            {
                try
                {
                    return _durationGetter?.Invoke() ?? TimeSpan.Zero;
                }
                catch
                {
                    return TimeSpan.Zero;
                }
            }
        }
    }

    public float Volume
    {
        get => Volatile.Read(ref _volume);
        set
        {
            var clamped = Math.Clamp(value, 0.0f, 1.0f);
            Volatile.Write(ref _volume, clamped);
            lock (_syncLock)
            {
                if (_toneProcessor is not null && !_isDoPActive)
                {
                    _toneProcessor.Volume = clamped;
                }
            }
        }
    }

    public WasapiPlaybackReport Play(
        string filePath,
        string? deviceId,
        string? deviceName,
        bool requestExclusive,
        int bufferMilliseconds,
        ToneControlSettings? toneSettings,
        DsdPlaybackOptions dsdOptions,
        TimeSpan? startPosition = null)
    {
        Stop();

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("No se encontró el archivo de audio.", filePath);
        }

        var latency = Math.Clamp(bufferMilliseconds, 30, 500);
        var device = ResolveEndpoint(deviceId, deviceName);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var isDsd = extension is ".dsf" or ".dff";

        try
        {
            WasapiPlaybackReport report;
            if (isDsd)
            {
                report = StartDsdPlayback(filePath, device, requestExclusive, latency, toneSettings, dsdOptions, startPosition);
            }
            else
            {
                report = StartPcmPlayback(filePath, device, requestExclusive, latency, toneSettings, startPosition);
            }

            ActiveReport = report;
            return report;
        }
        catch
        {
            Stop();
            device.Dispose();
            throw;
        }
    }

    public void Pause()
    {
        lock (_syncLock)
        {
            if (_wasapiOut?.PlaybackState == NAudio.Wave.PlaybackState.Playing)
            {
                _wasapiOut.Pause();
            }
        }
    }

    public void Resume()
    {
        lock (_syncLock)
        {
            if (_wasapiOut?.PlaybackState == NAudio.Wave.PlaybackState.Paused)
            {
                _toneProcessor?.ArmResumeRamp();
                _wasapiOut.Play();
            }
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (_syncLock)
        {
            var duration = _durationGetter?.Invoke() ?? TimeSpan.Zero;
            var clamped = duration > TimeSpan.Zero
                ? TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0.0, duration.TotalSeconds))
                : TimeSpan.Zero;

            if (_toneProcessor is not null && _seekAction is not null)
            {
                _toneProcessor.ExecuteSeekSafely(() => _seekAction(clamped));
            }
            else
            {
                _seekAction?.Invoke(clamped);
            }
        }
    }

    public void Stop()
    {
        WasapiOut? wasapi;
        BufferedAudioSampleProvider? buffered;
        IDisposable? source;
        MMDevice? device;

        lock (_syncLock)
        {
            _manualStop = true;
            wasapi = _wasapiOut;
            buffered = _bufferedSource;
            source = _activeSource;
            device = _activeDevice;
            _wasapiOut = null;
            _bufferedSource = null;
            _activeSource = null;
            _activeDevice = null;
            _seekAction = null;
            _positionGetter = null;
            _durationGetter = null;
            _toneProcessor = null;
            _isDoPActive = false;
            ActiveReport = null;
        }

        if (wasapi is not null)
        {
            wasapi.PlaybackStopped -= WasapiOut_PlaybackStopped;
            try
            {
                wasapi.Stop();
            }
            catch
            {
            }

            wasapi.Dispose();
        }

        try
        {
            buffered?.Dispose();
        }
        catch
        {
        }

        try
        {
            source?.Dispose();
        }
        catch
        {
        }

        try
        {
            device?.Dispose();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private WasapiPlaybackReport StartDsdPlayback(
        string filePath,
        MMDevice device,
        bool requestExclusive,
        int latencyMs,
        ToneControlSettings? toneSettings,
        DsdPlaybackOptions dsdOptions,
        TimeSpan? startPosition)
    {
        var metadata = DsdFileInspector.ReadMetadata(filePath);
        var dspActive = toneSettings?.IsActive == true;
        var volumeUnity = Volume >= 0.995f;
        var wantsDoP = dsdOptions.OutputMode == DsdOutputMode.DoPExclusive;
        var dopFallbackReason = string.Empty;

        if (wantsDoP && IsLikelyNonDoPDevice(device.FriendlyName))
        {
            dopFallbackReason = $"DoP bloqueado en '{device.FriendlyName}' (salida integrada/no-DAC) para evitar ruido digital";
            wantsDoP = false;
        }

        if (wantsDoP && requestExclusive && !dspActive && volumeUnity)
        {
            WaveFormat? supportedDoPFormat = null;
            var dop24In32 = new PcmExtensibleWaveFormat(metadata.DoPPcmSampleRate, 32, 24, metadata.Channels);
            var dop24Packed = new WaveFormat(metadata.DoPPcmSampleRate, 24, metadata.Channels);

            if (IsExclusiveFormatSupported(device, dop24In32))
            {
                supportedDoPFormat = dop24In32;
            }
            else if (IsExclusiveFormatSupported(device, dop24Packed))
            {
                supportedDoPFormat = dop24Packed;
            }

            if (supportedDoPFormat is not null)
            {
                var dopProvider = new DsdDoPWaveProvider(filePath, supportedDoPFormat, RaiseLevel);
                if (startPosition is { } pos && pos > TimeSpan.Zero)
                {
                    dopProvider.Seek(pos);
                }

                if (TryCreateAndInitWasapi(device, AudioClientShareMode.Exclusive, dopProvider, Math.Clamp(latencyMs, 50, 120), out var wasapiOut, out var isEventDriven))
                {
                    AttachAndPlay(
                        device,
                        wasapiOut!,
                        bufferedSource: null,
                        dopProvider,
                        toneProcessor: null,
                        isDoP: true,
                        seekAction: dopProvider.Seek,
                        positionGetter: () => dopProvider.Position,
                        durationGetter: () => dopProvider.Duration);

                    var dopContainer = supportedDoPFormat.BitsPerSample == 32 ? "24-en-32" : "24-bit";
                    return new WasapiPlaybackReport(
                        device.FriendlyName,
                        IsExclusive: true,
                        IsEventDriven: isEventDriven,
                        IsDoP: true,
                        IsDsdSource: true,
                        IsBitPerfect: true,
                        IsResampled: false,
                        OutputSampleRate: metadata.DoPPcmSampleRate,
                        OutputBitDepth: 24,
                        OutputChannels: metadata.Channels,
                        SourceCodec: metadata.DsdRateLabel,
                        DecodeLabel: $"DoP v1.1 ({metadata.DsdRateLabel}, {dopContainer})",
                        Summary: $"DoP v1.1 bit-perfect ({metadata.DsdRateLabel} @ {metadata.DoPPcmSampleRate / 1000.0:0.#} kHz {dopContainer}) en {device.FriendlyName}");
                }

                dopProvider.Dispose();
                dopFallbackReason = "El DAC rechazó la sesión DoP exclusiva";
            }
            else
            {
                dopFallbackReason = $"{device.FriendlyName} no soporta DoP {metadata.DoPPcmSampleRate / 1000.0:0.#} kHz 24-bit exclusivo";
            }
        }
        else if (dsdOptions.OutputMode == DsdOutputMode.DoPExclusive && string.IsNullOrWhiteSpace(dopFallbackReason))
        {
            if (!requestExclusive)
            {
                dopFallbackReason = "DoP requiere WASAPI Exclusivo activado";
            }
            else if (dspActive)
            {
                dopFallbackReason = "DoP requiere DSP/EQ omitido";
            }
            else if (!volumeUnity)
            {
                dopFallbackReason = "DoP requiere volumen al 100%";
            }
        }

        // High-precision multistage linear-phase FIR DSD -> PCM path
        var targetRate = ChooseBestDsdPcmRate(device, metadata, requestExclusive, dsdOptions.TargetPcmSampleRate);
        var dsdDecoder = new DsdPcmSampleProvider(filePath, targetRate, dsdOptions.GainDb);
        if (startPosition is { } seekPos && seekPos > TimeSpan.Zero)
        {
            dsdDecoder.Seek(seekPos);
        }

        var report = StartSampleProviderPipeline(
            device,
            dsdDecoder,
            dsdDecoder,
            requestExclusive,
            latencyMs,
            toneSettings,
            isDsdSource: true,
            sourceCodec: $"{metadata.DsdRateLabel} -> PCM FIR",
            decodeBaseLabel: $"FIR lineal ({metadata.DsdRateLabel}, {FormatGain(dsdOptions.GainDb)})",
            seekAction: dsdDecoder.Seek,
            positionGetter: () => dsdDecoder.Position,
            durationGetter: () => dsdDecoder.Duration);

        if (!string.IsNullOrWhiteSpace(dopFallbackReason))
        {
            report = report with
            {
                Summary = $"{dopFallbackReason}. Reproduciendo vía {report.DecodeLabel} ({report.OutputSampleRate / 1000.0:0.#} kHz/{report.OutputBitDepth}-bit) en {device.FriendlyName}"
            };
        }

        return report;
    }

    private WasapiPlaybackReport StartPcmPlayback(
        string filePath,
        MMDevice device,
        bool requestExclusive,
        int latencyMs,
        ToneControlSettings? toneSettings,
        TimeSpan? startPosition)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        WaveStream reader = extension switch
        {
            ".wav" => new WaveFileReader(filePath),
            ".aiff" or ".aif" => new AiffFileReader(filePath),
            _ => new MediaFoundationReader(filePath)
        };

        try
        {
            if (startPosition is { } pos && pos > TimeSpan.Zero && pos < reader.TotalTime)
            {
                reader.CurrentTime = pos;
            }

            var sampleProvider = reader.ToSampleProvider();
            var codecLabel = extension.TrimStart('.').ToUpperInvariant();
            return StartSampleProviderPipeline(
                device,
                reader,
                sampleProvider,
                requestExclusive,
                latencyMs,
                toneSettings,
                isDsdSource: false,
                sourceCodec: codecLabel,
                decodeBaseLabel: extension == ".wav" ? "PCM directo" : $"Decodificador {codecLabel}",
                seekAction: target =>
                {
                    var clamped = Math.Clamp(target.Ticks, 0, reader.TotalTime.Ticks);
                    reader.CurrentTime = TimeSpan.FromTicks(clamped);
                },
                positionGetter: () => reader.CurrentTime,
                durationGetter: () => reader.TotalTime);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private WasapiPlaybackReport StartSampleProviderPipeline(
        MMDevice device,
        IDisposable sourceOwner,
        ISampleProvider rawSamples,
        bool requestExclusive,
        int latencyMs,
        ToneControlSettings? toneSettings,
        bool isDsdSource,
        string sourceCodec,
        string decodeBaseLabel,
        Action<TimeSpan> seekAction,
        Func<TimeSpan> positionGetter,
        Func<TimeSpan> durationGetter)
    {
        // Decoupled software read-ahead ring buffer (~550 ms in RAM) + tight WASAPI hardware latency (45-90 ms)
        // like foobar2000: near-instant glitch-free seek and zero disk stalls on the real-time audio thread!
        var readAheadMs = Math.Clamp(latencyMs + 350, 450, 900);
        var hardwareLatencyMs = Math.Clamp(latencyMs, 45, 90);
        var buffered = new BufferedAudioSampleProvider(rawSamples, seekAction, positionGetter, durationGetter, readAheadMs);
        var toneProcessor = new ToneControlSampleProvider(buffered, toneSettings, RaiseLevel, Volume);
        var sourceRate = toneProcessor.WaveFormat.SampleRate;
        var sourceChannels = toneProcessor.WaveFormat.Channels;
        var dspActive = toneSettings?.IsActive == true;
        var volumeUnity = Volume >= 0.995f;

        try
        {
            if (requestExclusive)
            {
                // 1. Try native sample rate & channels first (Bit-Perfect!)
                if (TryStartExclusiveSampleProvider(device, toneProcessor, hardwareLatencyMs, out var exclusiveOut, out var exclusiveBits, out var isEventDriven))
                {
                    AttachAndPlay(device, exclusiveOut!, buffered, sourceOwner, toneProcessor, isDoP: false, buffered.RequestSeek, () => buffered.Position, durationGetter);
                    var isBitPerfect = !isDsdSource && !dspActive && volumeUnity;
                    var modeLabel = isEventDriven ? "WASAPI Exclusivo (Event)" : "WASAPI Exclusivo (Push)";
                    return new WasapiPlaybackReport(
                        device.FriendlyName,
                        IsExclusive: true,
                        IsEventDriven: isEventDriven,
                        IsDoP: false,
                        IsDsdSource: isDsdSource,
                        IsBitPerfect: isBitPerfect,
                        IsResampled: false,
                        OutputSampleRate: sourceRate,
                        OutputBitDepth: exclusiveBits,
                        OutputChannels: sourceChannels,
                        SourceCodec: sourceCodec,
                        DecodeLabel: decodeBaseLabel,
                        Summary: $"{modeLabel}: {sourceRate / 1000.0:0.#} kHz / {exclusiveBits}-bit en {device.FriendlyName}");
                }

                // 2. If native channels > 2 failed, try stereo downmix at native sample rate
                ISampleProvider exclusiveCandidate = toneProcessor;
                var outputChannels = sourceChannels;
                if (sourceChannels > 2)
                {
                    exclusiveCandidate = new StereoDownmixSampleProvider(toneProcessor);
                    outputChannels = 2;
                    if (TryStartExclusiveSampleProvider(device, exclusiveCandidate, hardwareLatencyMs, out var downmixOut, out var downmixBits, out var downmixEvent))
                    {
                        AttachAndPlay(device, downmixOut!, buffered, sourceOwner, toneProcessor, isDoP: false, buffered.RequestSeek, () => buffered.Position, durationGetter);
                        return new WasapiPlaybackReport(
                            device.FriendlyName,
                            IsExclusive: true,
                            IsEventDriven: downmixEvent,
                            IsDoP: false,
                            IsDsdSource: isDsdSource,
                            IsBitPerfect: false,
                            IsResampled: false,
                            OutputSampleRate: sourceRate,
                            OutputBitDepth: downmixBits,
                            OutputChannels: outputChannels,
                            SourceCodec: sourceCodec,
                            DecodeLabel: $"{decodeBaseLabel} (Estéreo)",
                            Summary: $"WASAPI Exclusivo (downmix estéreo): {sourceRate / 1000.0:0.#} kHz / {downmixBits}-bit en {device.FriendlyName}");
                    }
                }

                // 3. Try resampling cleanly to the highest exclusive rate supported by the endpoint
                var targetExclusiveRate = FindBestSupportedExclusiveRate(device, sourceRate, outputChannels);
                if (targetExclusiveRate > 0 && targetExclusiveRate != sourceRate)
                {
                    var resampled = new WdlResamplingSampleProvider(exclusiveCandidate, targetExclusiveRate);
                    if (TryStartExclusiveSampleProvider(device, resampled, hardwareLatencyMs, out var resampledOut, out var resampledBits, out var resampledEvent))
                    {
                        AttachAndPlay(device, resampledOut!, buffered, sourceOwner, toneProcessor, isDoP: false, buffered.RequestSeek, () => buffered.Position, durationGetter);
                        return new WasapiPlaybackReport(
                            device.FriendlyName,
                            IsExclusive: true,
                            IsEventDriven: resampledEvent,
                            IsDoP: false,
                            IsDsdSource: isDsdSource,
                            IsBitPerfect: false,
                            IsResampled: true,
                            OutputSampleRate: targetExclusiveRate,
                            OutputBitDepth: resampledBits,
                            OutputChannels: outputChannels,
                            SourceCodec: sourceCodec,
                            DecodeLabel: $"{decodeBaseLabel} -> {targetExclusiveRate / 1000.0:0.#} kHz",
                            Summary: $"WASAPI Exclusivo (adaptado a {targetExclusiveRate / 1000.0:0.#} kHz/{resampledBits}-bit) en {device.FriendlyName}");
                    }
                }
            }

            // WASAPI Shared path (reliable fallback or user-selected Shared mode)
            var mixFormat = device.AudioClient.MixFormat;
            ISampleProvider sharedPipeline = toneProcessor;
            var sharedResampled = false;

            if (sharedPipeline.WaveFormat.Channels > mixFormat.Channels && mixFormat.Channels == 2)
            {
                sharedPipeline = new StereoDownmixSampleProvider(sharedPipeline);
            }
            else if (sharedPipeline.WaveFormat.Channels == 1 && mixFormat.Channels >= 2)
            {
                sharedPipeline = new MonoToStereoSampleProvider(sharedPipeline);
            }

            if (sharedPipeline.WaveFormat.SampleRate != mixFormat.SampleRate)
            {
                sharedPipeline = new WdlResamplingSampleProvider(sharedPipeline, mixFormat.SampleRate);
                sharedResampled = true;
            }

            var sharedWave = sharedPipeline.ToWaveProvider();
            if (!TryCreateAndInitWasapi(device, AudioClientShareMode.Shared, sharedWave, hardwareLatencyMs, out var sharedOut, out var sharedEvent))
            {
                throw new InvalidOperationException($"No se pudo inicializar WASAPI en '{device.FriendlyName}'. Verifica que el dispositivo esté conectado y activo.");
            }

            AttachAndPlay(device, sharedOut!, buffered, sourceOwner, toneProcessor, isDoP: false, buffered.RequestSeek, () => buffered.Position, durationGetter);

            var sharedNote = requestExclusive
                ? $"Modo exclusivo no disponible en {device.FriendlyName}; usando WASAPI Compartido ({mixFormat.SampleRate / 1000.0:0.#} kHz/32-bit float)."
                : $"WASAPI Compartido: {mixFormat.SampleRate / 1000.0:0.#} kHz / 32-bit float en {device.FriendlyName}";

            return new WasapiPlaybackReport(
                device.FriendlyName,
                IsExclusive: false,
                IsEventDriven: sharedEvent,
                IsDoP: false,
                IsDsdSource: isDsdSource,
                IsBitPerfect: false,
                IsResampled: sharedResampled,
                OutputSampleRate: mixFormat.SampleRate,
                OutputBitDepth: 32,
                OutputChannels: sharedPipeline.WaveFormat.Channels,
                SourceCodec: sourceCodec,
                DecodeLabel: sharedResampled ? $"{decodeBaseLabel} -> {mixFormat.SampleRate / 1000.0:0.#} kHz" : decodeBaseLabel,
                Summary: sharedNote);
        }
        catch
        {
            buffered.Dispose();
            throw;
        }
    }

    private void AttachAndPlay(
        MMDevice device,
        WasapiOut wasapiOut,
        BufferedAudioSampleProvider? bufferedSource,
        IDisposable sourceOwner,
        ToneControlSampleProvider? toneProcessor,
        bool isDoP,
        Action<TimeSpan> seekAction,
        Func<TimeSpan> positionGetter,
        Func<TimeSpan> durationGetter)
    {
        lock (_syncLock)
        {
            _manualStop = false;
            _activeDevice = device;
            _wasapiOut = wasapiOut;
            _bufferedSource = bufferedSource;
            _activeSource = sourceOwner;
            _toneProcessor = toneProcessor;
            _isDoPActive = isDoP;
            _seekAction = seekAction;
            _positionGetter = positionGetter;
            _durationGetter = durationGetter;
            wasapiOut.PlaybackStopped += WasapiOut_PlaybackStopped;
            wasapiOut.Play();
        }
    }

    private void WasapiOut_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        bool shouldRaiseEnded;
        lock (_syncLock)
        {
            shouldRaiseEnded = !_manualStop && ReferenceEquals(sender, _wasapiOut);
        }

        if (shouldRaiseEnded)
        {
            TrackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RaiseLevel(double level)
    {
        LevelChanged?.Invoke(this, level);
    }

    private static int ChooseBestDsdPcmRate(MMDevice device, DsdStreamMetadata metadata, bool requestExclusive, int requestedRate)
    {
        var baseFamily = metadata.BaseFamilyRate;
        if (requestedRate > 0)
        {
            return DsdPcmSampleProvider.ResolveTargetSampleRate(requestedRate, baseFamily);
        }

        // Auto mode (like foobar2000): pick highest clean DSD multiple supported by the endpoint
        if (requestExclusive)
        {
            var candidates = new[] { baseFamily * 4, baseFamily * 2, baseFamily }; // 176.4k -> 88.2k -> 44.1k (or 192/96/48)
            var channels = Math.Min(metadata.Channels, 2);
            foreach (var rate in candidates)
            {
                if (IsExclusiveFormatSupported(device, new PcmExtensibleWaveFormat(rate, 32, 32, channels)) ||
                    IsExclusiveFormatSupported(device, new PcmExtensibleWaveFormat(rate, 32, 24, channels)) ||
                    IsExclusiveFormatSupported(device, WaveFormat.CreateIeeeFloatWaveFormat(rate, channels)) ||
                    IsExclusiveFormatSupported(device, new WaveFormat(rate, 24, channels)) ||
                    IsExclusiveFormatSupported(device, new WaveFormat(rate, 16, channels)))
                {
                    return rate;
                }
            }
        }

        // In Shared mode or fallback, 88.2 kHz (or 96 kHz for 48k DSD) gives great clarity with low CPU
        return baseFamily * 2;
    }

    private static bool TryStartExclusiveSampleProvider(
        MMDevice device,
        ISampleProvider samples,
        int latencyMs,
        out WasapiOut? wasapiOut,
        out int bitDepth,
        out bool isEventDriven)
    {
        var rate = samples.WaveFormat.SampleRate;
        var channels = samples.WaveFormat.Channels;

        // Order of preference for Exclusive output using exact format-matched providers:
        // 1. 32-bit PCM Extensible (KSDATAFORMAT_SUBTYPE_PCM - native format for FiiO/Thesycon/XMOS USB Audio 2.0 DACs)
        // 2. 24-in-32 PCM Extensible (KSDATAFORMAT_SUBTYPE_PCM)
        // 3. 32-bit IEEE Float
        // 4. 24-bit packed PCM
        // 5. 16-bit PCM (universal fallback)
        var pcm32Ext = new PcmExtensibleWaveFormat(rate, 32, 32, channels);
        var pcm24In32Ext = new PcmExtensibleWaveFormat(rate, 32, 24, channels);
        var float32Format = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        var pcm24Format = new WaveFormat(rate, 24, channels);
        var pcm16Format = new WaveFormat(rate, 16, channels);

        var attempts = new (int Bits, Func<IWaveProvider> Factory, WaveFormat ProbeFormat)[]
        {
            (32, () => new SampleToPcm32ExtensibleWaveProvider(samples, pcm32Ext), pcm32Ext),
            (24, () => new SampleToPcm32ExtensibleWaveProvider(samples, pcm24In32Ext, maskTo24Bits: true), pcm24In32Ext),
            (32, () => samples.ToWaveProvider(), float32Format),
            (24, () => new SampleToWaveProvider24(samples), pcm24Format),
            (16, () => new SampleToWaveProvider16(samples), pcm16Format)
        };

        foreach (var attempt in attempts)
        {
            if (!IsExclusiveFormatSupported(device, attempt.ProbeFormat))
            {
                continue;
            }

            if (TryCreateAndInitWasapi(device, AudioClientShareMode.Exclusive, attempt.Factory(), latencyMs, out wasapiOut, out isEventDriven))
            {
                bitDepth = attempt.Bits;
                return true;
            }
        }

        wasapiOut = null;
        bitDepth = 0;
        isEventDriven = false;
        return false;
    }

    private static int FindBestSupportedExclusiveRate(MMDevice device, int sourceRate, int channels)
    {
        var family44 = sourceRate % 44100 == 0;
        var candidates = family44
            ? new[] { 176400, 88200, 44100, 192000, 96000, 48000 }
            : new[] { 192000, 96000, 48000, 176400, 88200, 44100 };

        // Prefer rates >= sourceRate first, then highest available below sourceRate
        var ordered = candidates
            .OrderBy(rate => rate < sourceRate ? 1 : 0)
            .ThenBy(rate => Math.Abs(rate - sourceRate));

        foreach (var rate in ordered)
        {
            if (IsExclusiveFormatSupported(device, new PcmExtensibleWaveFormat(rate, 32, 32, channels)) ||
                IsExclusiveFormatSupported(device, new PcmExtensibleWaveFormat(rate, 32, 24, channels)) ||
                IsExclusiveFormatSupported(device, WaveFormat.CreateIeeeFloatWaveFormat(rate, channels)) ||
                IsExclusiveFormatSupported(device, new WaveFormat(rate, 24, channels)) ||
                IsExclusiveFormatSupported(device, new WaveFormat(rate, 16, channels)))
            {
                return rate;
            }
        }

        return 0;
    }

    private static bool IsExclusiveFormatSupported(MMDevice device, WaveFormat format)
    {
        try
        {
            return device.AudioClient.IsFormatSupported(AudioClientShareMode.Exclusive, format);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreateAndInitWasapi(
        MMDevice device,
        AudioClientShareMode shareMode,
        IWaveProvider waveProvider,
        int latencyMs,
        out WasapiOut? wasapiOut,
        out bool isEventDriven)
    {
        foreach (var useEventSync in new[] { true, false })
        {
            WasapiOut? candidate = null;
            try
            {
                candidate = new WasapiOut(device, shareMode, useEventSync, latencyMs);
                candidate.Init(waveProvider);
                wasapiOut = candidate;
                isEventDriven = useEventSync;
                return true;
            }
            catch
            {
                candidate?.Dispose();
            }
        }

        wasapiOut = null;
        isEventDriven = false;
        return false;
    }

    private static MMDevice ResolveEndpoint(string? deviceId, string? deviceName)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(deviceId) || !string.IsNullOrWhiteSpace(deviceName))
        {
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            MMDevice? matched = null;

            foreach (var endpoint in endpoints)
            {
                if (!string.IsNullOrWhiteSpace(deviceId) &&
                    (endpoint.ID.Equals(deviceId, StringComparison.OrdinalIgnoreCase) ||
                     deviceId.Contains(endpoint.ID, StringComparison.OrdinalIgnoreCase) ||
                     endpoint.ID.Contains(deviceId, StringComparison.OrdinalIgnoreCase)))
                {
                    matched = endpoint;
                    break;
                }
            }

            if (matched is null && !string.IsNullOrWhiteSpace(deviceName))
            {
                var normalizedTarget = NormalizeName(deviceName);
                foreach (var endpoint in endpoints)
                {
                    var name = endpoint.FriendlyName;
                    if (name.Equals(deviceName, StringComparison.OrdinalIgnoreCase) ||
                        name.Contains(deviceName, StringComparison.OrdinalIgnoreCase) ||
                        deviceName.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                        NormalizeName(name).Contains(normalizedTarget, StringComparison.Ordinal) ||
                        normalizedTarget.Contains(NormalizeName(name), StringComparison.Ordinal))
                    {
                        matched = endpoint;
                        break;
                    }
                }
            }

            foreach (var endpoint in endpoints)
            {
                if (!ReferenceEquals(endpoint, matched))
                {
                    endpoint.Dispose();
                }
            }

            if (matched is not null)
            {
                return matched;
            }
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    private static string NormalizeName(string value)
    {
        return new string(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    public static WasapiEndpointDriverInfo InspectEndpointDriver(string? deviceId, string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceId) && string.IsNullOrWhiteSpace(deviceName))
        {
            return new WasapiEndpointDriverInfo(false, "Windows", 0);
        }

        try
        {
            using var device = ResolveEndpoint(deviceId, deviceName);
            var friendlyName = device.FriendlyName ?? deviceName ?? string.Empty;
            var mixRate = 0;
            try
            {
                mixRate = device.AudioClient.MixFormat.SampleRate;
            }
            catch
            {
            }

            if (IsLikelyNonDoPDevice(friendlyName))
            {
                return new WasapiEndpointDriverInfo(false, "Integrado", mixRate);
            }

            var propValues = new List<string> { friendlyName };
            try
            {
                var props = device.Properties;
                for (var i = 0; i < props.Count; i++)
                {
                    if (props[i].Value is string text && !string.IsNullOrWhiteSpace(text))
                    {
                        propValues.Add(text);
                    }
                }
            }
            catch
            {
            }

            var combined = string.Join(" ", propValues).ToLowerInvariant();

            if (combined.Contains("tusbaudio", StringComparison.Ordinal) ||
                combined.Contains("vid_2972", StringComparison.Ordinal) ||
                combined.Contains("fiio", StringComparison.Ordinal) ||
                combined.Contains("jadeaudio", StringComparison.Ordinal) ||
                combined.Contains("ja11", StringComparison.Ordinal))
            {
                return new WasapiEndpointDriverInfo(true, "FiiO / USB DAC", mixRate);
            }

            if (combined.Contains("usbaudio2", StringComparison.Ordinal) ||
                combined.Contains("thesycon", StringComparison.Ordinal) ||
                combined.Contains("xmos", StringComparison.Ordinal) ||
                combined.Contains("savitech", StringComparison.Ordinal) ||
                combined.Contains("bravo-hd", StringComparison.Ordinal) ||
                combined.Contains("zishan", StringComparison.Ordinal) ||
                combined.Contains("asio", StringComparison.Ordinal) ||
                combined.Contains("(2.0)", StringComparison.Ordinal) ||
                mixRate >= 352800)
            {
                return new WasapiEndpointDriverInfo(true, "USB Audio 2.0 DAC", mixRate);
            }

            return new WasapiEndpointDriverInfo(false, "WASAPI", mixRate);
        }
        catch
        {
            return new WasapiEndpointDriverInfo(false, "WASAPI", 0);
        }
    }

    private static bool IsLikelyNonDoPDevice(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return true;
        }

        return deviceName.Contains("Realtek", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("High Definition Audio", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("Conexant", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("IDT", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("AMD High Definition", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("Gaming Headset", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("Redragon", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("HyperX", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("Logitech", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("Razer", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("Corsair", StringComparison.OrdinalIgnoreCase) ||
               deviceName.Contains("SteelSeries", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatGain(double gainDb)
    {
        return gainDb >= 0 ? $"+{gainDb:0.#} dB" : $"{gainDb:0.#} dB";
    }

    /// <summary>
    /// Win32 WAVEFORMATEXTENSIBLE with explicit KSDATAFORMAT_SUBTYPE_PCM (00000001-0000-0010-8000-00aa00389b71).
    /// Unlike NAudio's built-in WaveFormatExtensible(rate, 32, ch) which defaults 32-bit to IEEE_FLOAT,
    /// this emits genuine integer PCM required by Thesycon/FiiO/XMOS USB Audio 2.0 drivers in Exclusive mode.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 2)]
    private sealed class PcmExtensibleWaveFormat : WaveFormat
    {
        private static readonly Guid KsDataFormatSubtypePcm = new("00000001-0000-0010-8000-00aa00389b71");
        private readonly short _wValidBitsPerSample;
        private readonly int _dwChannelMask;
        private readonly Guid _subFormat;

        public PcmExtensibleWaveFormat(int sampleRate, int containerBits, int validBits, int channels)
            : base(sampleRate, containerBits, channels)
        {
            waveFormatTag = WaveFormatEncoding.Extensible;
            extraSize = 22;
            _wValidBitsPerSample = (short)validBits;
            _dwChannelMask = channels switch
            {
                1 => 0x00000004,
                2 => 0x00000003,
                4 => 0x00000033,
                6 => 0x0000003F,
                8 => 0x0000063F,
                _ => 0
            };
            _subFormat = KsDataFormatSubtypePcm;
        }
    }

    private sealed class SampleToPcm32ExtensibleWaveProvider : IWaveProvider
    {
        private readonly ISampleProvider _source;
        private readonly bool _maskTo24Bits;
        private float[] _floatBuffer = [];

        public SampleToPcm32ExtensibleWaveProvider(ISampleProvider source, PcmExtensibleWaveFormat targetFormat, bool maskTo24Bits = false)
        {
            _source = source;
            _maskTo24Bits = maskTo24Bits;
            WaveFormat = targetFormat;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            var samplesRequested = count / 4;
            if (samplesRequested <= 0)
            {
                return 0;
            }

            if (_floatBuffer.Length < samplesRequested)
            {
                _floatBuffer = new float[samplesRequested];
            }

            var samplesRead = _source.Read(_floatBuffer, 0, samplesRequested);
            for (var i = 0; i < samplesRead; i++)
            {
                var clamped = Math.Clamp(_floatBuffer[i], -1.0f, 1.0f);
                var sample32 = (int)Math.Round(clamped * 2147483647.0);
                if (_maskTo24Bits)
                {
                    sample32 &= unchecked((int)0xFFFFFF00);
                }

                var dest = offset + (i * 4);
                buffer[dest] = (byte)(sample32 & 0xFF);
                buffer[dest + 1] = (byte)((sample32 >> 8) & 0xFF);
                buffer[dest + 2] = (byte)((sample32 >> 16) & 0xFF);
                buffer[dest + 3] = (byte)((sample32 >> 24) & 0xFF);
            }

            return samplesRead * 4;
        }
    }

    private sealed class StereoDownmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _sourceChannels;
        private float[] _tempBuffer = [];

        public StereoDownmixSampleProvider(ISampleProvider source)
        {
            _source = source;
            _sourceChannels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var framesRequested = count / 2;
            var sourceSamplesNeeded = framesRequested * _sourceChannels;
            if (_tempBuffer.Length < sourceSamplesNeeded)
            {
                _tempBuffer = new float[sourceSamplesNeeded];
            }

            var sourceRead = _source.Read(_tempBuffer, 0, sourceSamplesNeeded);
            var framesRead = sourceRead / _sourceChannels;

            for (var frame = 0; frame < framesRead; frame++)
            {
                var src = frame * _sourceChannels;
                var left = _tempBuffer[src];
                var right = _sourceChannels > 1 ? _tempBuffer[src + 1] : left;
                if (_sourceChannels > 2)
                {
                    var center = _tempBuffer[src + 2] * 0.7071f;
                    left = (left + center) * 0.75f;
                    right = (right + center) * 0.75f;
                }

                if (_sourceChannels >= 6)
                {
                    left += _tempBuffer[src + 4] * 0.35f;
                    right += _tempBuffer[src + 5] * 0.35f;
                }

                buffer[offset + (frame * 2)] = Math.Clamp(left, -1.0f, 1.0f);
                buffer[offset + (frame * 2) + 1] = Math.Clamp(right, -1.0f, 1.0f);
            }

            return framesRead * 2;
        }
    }
}
