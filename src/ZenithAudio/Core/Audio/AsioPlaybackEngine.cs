using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ZenithAudio.Core.Audio;

public sealed record AsioDriverInfo(string Name, string Description, Guid Clsid)
{
    public bool IsHardwareDriver =>
        !Name.Contains("foo_dsd_asio", StringComparison.OrdinalIgnoreCase) &&
        !Name.Contains("asio4all", StringComparison.OrdinalIgnoreCase);

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Description) || Description.Equals(Name, StringComparison.OrdinalIgnoreCase)
            ? Name
            : $"{Name} ({Description})";
    }
}

public sealed record AsioPlaybackReport(
    string DriverName,
    bool IsNativeDsd,
    bool IsBitPerfect,
    bool IsResampled,
    int SampleRate,
    int BitDepth,
    int Channels,
    string SourceCodec,
    string DecodeLabel,
    string Summary);

public sealed class AsioPlaybackEngine : IDisposable
{
    private readonly object _syncLock = new();
    private NativeAsioDsdSession? _nativeDsdSession;
    private AsioOut? _pcmAsioOut;
    private IDisposable? _pcmSourceOwner;
    private ToneControlSampleProvider? _toneProcessor;
    private Action<TimeSpan>? _seekAction;
    private Func<TimeSpan>? _positionGetter;
    private Func<TimeSpan>? _durationGetter;
    private bool _manualStop;
    private bool _isNativeDsdActive;
    private float _volume = 1.0f;

    public event EventHandler? TrackEnded;

    public event EventHandler<double>? LevelChanged;

    public AsioPlaybackReport? ActiveReport { get; private set; }

    public bool IsNativeDsdActive => _isNativeDsdActive;

    public bool IsPlaying
    {
        get
        {
            lock (_syncLock)
            {
                if (_nativeDsdSession is not null)
                {
                    return !_nativeDsdSession.IsPaused;
                }

                return _pcmAsioOut?.PlaybackState == NAudio.Wave.PlaybackState.Playing;
            }
        }
    }

    public TimeSpan Position
    {
        get
        {
            lock (_syncLock)
            {
                try
                {
                    if (_nativeDsdSession is not null)
                    {
                        return _nativeDsdSession.Position;
                    }

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
                    if (_nativeDsdSession is not null)
                    {
                        return _nativeDsdSession.Duration;
                    }

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
                if (_toneProcessor is not null && !_isNativeDsdActive)
                {
                    _toneProcessor.Volume = clamped;
                }
            }
        }
    }

    public static List<AsioDriverInfo> GetAvailableDrivers()
    {
        var drivers = new List<AsioDriverInfo>();
        try
        {
            using var asioKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ASIO");
            if (asioKey is null)
            {
                return drivers;
            }

            foreach (var subKeyName in asioKey.GetSubKeyNames())
            {
                using var driverKey = asioKey.OpenSubKey(subKeyName);
                var clsidString = driverKey?.GetValue("CLSID") as string;
                if (string.IsNullOrWhiteSpace(clsidString) || !Guid.TryParse(clsidString, out var clsid))
                {
                    continue;
                }

                var description = driverKey?.GetValue("Description") as string ?? subKeyName;
                drivers.Add(new AsioDriverInfo(subKeyName, description, clsid));
            }
        }
        catch
        {
        }

        return drivers
            .OrderByDescending(d => d.IsHardwareDriver)
            .ThenByDescending(d => d.Name.Contains("FiiO", StringComparison.OrdinalIgnoreCase))
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static void OpenControlPanel(string driverName, IntPtr windowHandle)
    {
        var drivers = GetAvailableDrivers();
        var driver = drivers.FirstOrDefault(d => d.Name.Equals(driverName, StringComparison.OrdinalIgnoreCase));
        if (driver is null)
        {
            throw new InvalidOperationException($"No se encontró el driver ASIO '{driverName}'.");
        }

        if (NativeAsioDsdSession.TryOpenControlPanel(driver.Clsid, windowHandle))
        {
            return;
        }

        using var asioOut = new AsioOut(driver.Name);
        asioOut.ShowControlPanel();
    }

    /// <summary>
    /// Must be called on an STA thread (such as the WinUI 3 UI thread) with a valid window handle.
    /// </summary>
    public AsioPlaybackReport Play(
        string filePath,
        string driverName,
        IntPtr windowHandle,
        ToneControlSettings? toneSettings,
        DsdPlaybackOptions dsdOptions,
        TimeSpan? startPosition = null)
    {
        Stop();

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("No se encontró el archivo de audio.", filePath);
        }

        var driver = GetAvailableDrivers()
            .FirstOrDefault(d => d.Name.Equals(driverName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No se encontró el controlador ASIO '{driverName}' en el sistema.");

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var isDsd = extension is ".dsf" or ".dff";

        if (isDsd)
        {
            var dspActive = toneSettings?.IsActive == true;
            var fallbackReason = string.Empty;

            if (!dspActive)
            {
                try
                {
                    var session = new NativeAsioDsdSession(
                        driver,
                        filePath,
                        windowHandle,
                        startPosition,
                        RaiseLevel,
                        RaiseNativeDsdEnded);

                    lock (_syncLock)
                    {
                        _manualStop = false;
                        _nativeDsdSession = session;
                        _isNativeDsdActive = true;
                    }

                    var mhz = session.Metadata.SampleRate / 1_000_000.0;
                    var report = new AsioPlaybackReport(
                        driver.Name,
                        IsNativeDsd: true,
                        IsBitPerfect: true,
                        IsResampled: false,
                        SampleRate: session.Metadata.SampleRate,
                        BitDepth: 1,
                        Channels: session.ActiveChannels,
                        SourceCodec: session.Metadata.DsdRateLabel,
                        DecodeLabel: $"ASIO DSD Nativo ({session.Metadata.DsdRateLabel}, {session.SampleTypeLabel})",
                        Summary: $"ASIO DSD Nativo bit-perfect: {session.Metadata.DsdRateLabel} ({mhz:0.##} MHz / 1-bit) en {driver.Name}");
                    ActiveReport = report;
                    return report;
                }
                catch (Exception ex)
                {
                    fallbackReason = ex.Message;
                }
            }
            else
            {
                fallbackReason = "DSP/EQ activo (DSD nativo requiere DSP omitido)";
            }

            // Fallback: high-precision multistage FIR DSD -> PCM over ASIO
            var dsdPcmReport = StartDsdPcmOverAsio(filePath, driver, toneSettings, dsdOptions, startPosition, fallbackReason);
            ActiveReport = dsdPcmReport;
            return dsdPcmReport;
        }

        var pcmReport = StartPcmOverAsio(filePath, driver, toneSettings, startPosition);
        ActiveReport = pcmReport;
        return pcmReport;
    }

    public void Pause()
    {
        lock (_syncLock)
        {
            if (_nativeDsdSession is not null)
            {
                _nativeDsdSession.IsPaused = true;
                return;
            }

            if (_pcmAsioOut?.PlaybackState == NAudio.Wave.PlaybackState.Playing)
            {
                _pcmAsioOut.Pause();
            }
        }
    }

    public void Resume()
    {
        lock (_syncLock)
        {
            if (_nativeDsdSession is not null)
            {
                _nativeDsdSession.IsPaused = false;
                return;
            }

            if (_pcmAsioOut?.PlaybackState == NAudio.Wave.PlaybackState.Paused)
            {
                _pcmAsioOut.Play();
            }
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (_syncLock)
        {
            if (_nativeDsdSession is not null)
            {
                _nativeDsdSession.Seek(position);
                return;
            }

            var duration = _durationGetter?.Invoke() ?? TimeSpan.Zero;
            var clamped = duration > TimeSpan.Zero
                ? TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0.0, duration.TotalSeconds))
                : TimeSpan.Zero;
            _seekAction?.Invoke(clamped);
        }
    }

    public void Stop()
    {
        NativeAsioDsdSession? dsdSession;
        AsioOut? pcmAsio;
        IDisposable? pcmSource;

        lock (_syncLock)
        {
            _manualStop = true;
            dsdSession = _nativeDsdSession;
            pcmAsio = _pcmAsioOut;
            pcmSource = _pcmSourceOwner;
            _nativeDsdSession = null;
            _pcmAsioOut = null;
            _pcmSourceOwner = null;
            _toneProcessor = null;
            _seekAction = null;
            _positionGetter = null;
            _durationGetter = null;
            _isNativeDsdActive = false;
            ActiveReport = null;
        }

        try
        {
            dsdSession?.Dispose();
        }
        catch
        {
        }

        if (pcmAsio is not null)
        {
            pcmAsio.PlaybackStopped -= PcmAsioOut_PlaybackStopped;
            try
            {
                pcmAsio.Stop();
            }
            catch
            {
            }

            try
            {
                pcmAsio.Dispose();
            }
            catch
            {
            }
        }

        try
        {
            pcmSource?.Dispose();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private AsioPlaybackReport StartDsdPcmOverAsio(
        string filePath,
        AsioDriverInfo driver,
        ToneControlSettings? toneSettings,
        DsdPlaybackOptions dsdOptions,
        TimeSpan? startPosition,
        string fallbackReason)
    {
        var metadata = DsdFileInspector.ReadMetadata(filePath);
        var targetRate = dsdOptions.TargetPcmSampleRate > 0
            ? DsdPcmSampleProvider.ResolveTargetSampleRate(dsdOptions.TargetPcmSampleRate, metadata.BaseFamilyRate)
            : metadata.BaseFamilyRate * 4; // 176.4 kHz or 192 kHz

        var dsdDecoder = new DsdPcmSampleProvider(filePath, targetRate, dsdOptions.GainDb);
        if (startPosition is { } pos && pos > TimeSpan.Zero)
        {
            dsdDecoder.Seek(pos);
        }

        try
        {
            var report = InitPcmAsioPipeline(
                driver,
                dsdDecoder,
                dsdDecoder,
                toneSettings,
                isDsdSource: true,
                sourceCodec: $"{metadata.DsdRateLabel} -> ASIO PCM FIR",
                decodeBaseLabel: $"ASIO FIR lineal ({metadata.DsdRateLabel})",
                seekAction: dsdDecoder.Seek,
                positionGetter: () => dsdDecoder.Position,
                durationGetter: () => dsdDecoder.Duration);

            if (!string.IsNullOrWhiteSpace(fallbackReason))
            {
                report = report with
                {
                    Summary = $"{fallbackReason}. Reproduciendo por {driver.Name} en ASIO PCM FIR ({report.SampleRate / 1000.0:0.#} kHz/32-bit)."
                };
            }

            return report;
        }
        catch
        {
            dsdDecoder.Dispose();
            throw;
        }
    }

    private AsioPlaybackReport StartPcmOverAsio(
        string filePath,
        AsioDriverInfo driver,
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

            var codecLabel = extension.TrimStart('.').ToUpperInvariant();
            return InitPcmAsioPipeline(
                driver,
                reader,
                reader.ToSampleProvider(),
                toneSettings,
                isDsdSource: false,
                sourceCodec: codecLabel,
                decodeBaseLabel: $"ASIO PCM ({codecLabel})",
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

    private AsioPlaybackReport InitPcmAsioPipeline(
        AsioDriverInfo driver,
        IDisposable sourceOwner,
        ISampleProvider rawSamples,
        ToneControlSettings? toneSettings,
        bool isDsdSource,
        string sourceCodec,
        string decodeBaseLabel,
        Action<TimeSpan> seekAction,
        Func<TimeSpan> positionGetter,
        Func<TimeSpan> durationGetter)
    {
        var toneProcessor = new ToneControlSampleProvider(rawSamples, toneSettings, RaiseLevel, Volume);
        var asioOut = new AsioOut(driver.Name);

        try
        {
            ISampleProvider pipeline = toneProcessor;
            var sourceRate = pipeline.WaveFormat.SampleRate;
            var outputRate = sourceRate;
            var resampled = false;

            if (!asioOut.IsSampleRateSupported(sourceRate))
            {
                var bestRate = FindBestAsioSampleRate(asioOut, sourceRate);
                if (bestRate <= 0)
                {
                    throw new InvalidOperationException($"El driver ASIO '{driver.Name}' no acepta {sourceRate} Hz.");
                }

                pipeline = new WdlResamplingSampleProvider(pipeline, bestRate);
                outputRate = bestRate;
                resampled = true;
            }

            if (pipeline.WaveFormat.Channels == 1 && asioOut.DriverOutputChannelCount >= 2)
            {
                pipeline = new MonoToStereoSampleProvider(pipeline);
            }

            asioOut.Init(pipeline);
            asioOut.PlaybackStopped += PcmAsioOut_PlaybackStopped;

            lock (_syncLock)
            {
                _manualStop = false;
                _pcmAsioOut = asioOut;
                _pcmSourceOwner = sourceOwner;
                _toneProcessor = toneProcessor;
                _isNativeDsdActive = false;
                _seekAction = seekAction;
                _positionGetter = positionGetter;
                _durationGetter = durationGetter;
            }

            asioOut.Play();

            var dspActive = toneSettings?.IsActive == true;
            var volumeUnity = Volume >= 0.995f;
            var bitPerfect = !isDsdSource && !resampled && !dspActive && volumeUnity;

            return new AsioPlaybackReport(
                driver.Name,
                IsNativeDsd: false,
                IsBitPerfect: bitPerfect,
                IsResampled: resampled,
                SampleRate: outputRate,
                BitDepth: 32,
                Channels: pipeline.WaveFormat.Channels,
                SourceCodec: sourceCodec,
                DecodeLabel: resampled ? $"{decodeBaseLabel} -> {outputRate / 1000.0:0.#} kHz" : decodeBaseLabel,
                Summary: $"ASIO PCM: {outputRate / 1000.0:0.#} kHz / 32-bit en {driver.Name}");
        }
        catch
        {
            asioOut.Dispose();
            throw;
        }
    }

    private static int FindBestAsioSampleRate(AsioOut asioOut, int sourceRate)
    {
        var family44 = sourceRate % 44100 == 0;
        var candidates = family44
            ? new[] { 352800, 176400, 88200, 44100, 384000, 192000, 96000, 48000 }
            : new[] { 384000, 192000, 96000, 48000, 352800, 176400, 88200, 44100 };

        foreach (var rate in candidates.OrderBy(r => r < sourceRate ? 1 : 0).ThenBy(r => Math.Abs(r - sourceRate)))
        {
            try
            {
                if (asioOut.IsSampleRateSupported(rate))
                {
                    return rate;
                }
            }
            catch
            {
            }
        }

        return 0;
    }

    private void PcmAsioOut_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        bool shouldRaise;
        lock (_syncLock)
        {
            shouldRaise = !_manualStop && ReferenceEquals(sender, _pcmAsioOut);
        }

        if (shouldRaise)
        {
            TrackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RaiseNativeDsdEnded()
    {
        bool shouldRaise;
        lock (_syncLock)
        {
            shouldRaise = !_manualStop && _nativeDsdSession is not null;
        }

        if (shouldRaise)
        {
            TrackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RaiseLevel(double level)
    {
        LevelChanged?.Invoke(this, level);
    }

    /// <summary>
    /// Native Steinberg ASIO 2.1 DSD COM session (win-x64).
    /// Switches the ASIO driver to kASIODSDFormat via Future(kAsioSetIoFormat) and streams raw 1-bit DSD bytes directly.
    /// </summary>
    private sealed unsafe class NativeAsioDsdSession : IDisposable
    {
        private const int AseOk = 0;
        private const int AseSuccess = 0x3f4847a0;
        private const int KAsioSetIoFormat = 0x23111961;
        private const int KAsioCanDoIoFormat = 0x23112004;
        private const int KAsioPcmFormat = 0;
        private const int KAsioDsdFormat = 1;
        private const int AsioStDsdInt8Lsb1 = 32;
        private const int AsioStDsdInt8Msb1 = 33;
        private const byte DsdSilenceByte = 0x69;

        private static readonly byte[] BitReverseTable = BuildBitReverseTable();

        private readonly DsdByteReader _reader;
        private readonly Action<double>? _levelSink;
        private readonly Action _endedCallback;
        private readonly object _readLock = new();
        private IntPtr _asioPtr;
        private IntPtr* _vtable;
        private IntPtr _bufferInfosPtr;
        private IntPtr _callbacksPtr;
        private int _activeChannels;
        private int _bufferBytesPerChannel;
        private bool _reverseBits;
        private bool _buffersCreated;
        private bool _started;
        private bool _switchedToDsd;
        private bool _isPaused;
        private bool _endedSignaled;
        private bool _disposed;

        // Keep callback delegates alive for the entire ASIO session lifetime
        private readonly BufferSwitchDelegate _bufferSwitchDelegate;
        private readonly SampleRateDidChangeDelegate _sampleRateDidChangeDelegate;
        private readonly AsioMessageDelegate _asioMessageDelegate;
        private readonly BufferSwitchTimeInfoDelegate _bufferSwitchTimeInfoDelegate;

        public NativeAsioDsdSession(
            AsioDriverInfo driver,
            string filePath,
            IntPtr windowHandle,
            TimeSpan? startPosition,
            Action<double>? levelSink,
            Action endedCallback)
        {
            _levelSink = levelSink;
            _endedCallback = endedCallback;
            _reader = new DsdByteReader(filePath);
            Metadata = _reader.Metadata;

            _bufferSwitchDelegate = OnBufferSwitch;
            _sampleRateDidChangeDelegate = OnSampleRateDidChange;
            _asioMessageDelegate = OnAsioMessage;
            _bufferSwitchTimeInfoDelegate = OnBufferSwitchTimeInfo;

            try
            {
                var clsid = driver.Clsid;
                var iid = driver.Clsid;
                var hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, ref iid, out _asioPtr);
                if (hr < 0 || _asioPtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException($"No se pudo crear la instancia COM de '{driver.Name}' (HRESULT 0x{hr:X8}).");
                }

                _vtable = *(IntPtr**)_asioPtr;

                if (CallInit(windowHandle) == 0)
                {
                    throw new InvalidOperationException($"'{driver.Name}' no pudo inicializarse. Verifica que el DAC esté conectado.");
                }

                // Switch driver to Steinberg ASIO 2.1 DSD mode
                var dsdFormat = new AsioIoFormat { FormatType = KAsioDsdFormat };
                var canDo = CallFuture(KAsioCanDoIoFormat, &dsdFormat);
                if (canDo != AseSuccess && canDo != AseOk)
                {
                    throw new InvalidOperationException($"'{driver.Name}' no anuncia soporte ASIO DSD nativo.");
                }

                var setFmt = CallFuture(KAsioSetIoFormat, &dsdFormat);
                if (setFmt != AseSuccess && setFmt != AseOk)
                {
                    throw new InvalidOperationException($"'{driver.Name}' rechazó el cambio a modo ASIO DSD (código {setFmt}).");
                }

                _switchedToDsd = true;

                // Validate and set exact native DSD sample rate (e.g., 2822400 Hz or 5644800 Hz)
                var rate = (double)Metadata.SampleRate;
                if (CallCanSampleRate(rate) != AseOk)
                {
                    throw new InvalidOperationException($"'{driver.Name}' no soporta {Metadata.DsdRateLabel} ({Metadata.SampleRate} Hz) nativo.");
                }

                var setRateErr = CallSetSampleRate(rate);
                if (setRateErr != AseOk)
                {
                    throw new InvalidOperationException($"'{driver.Name}' no pudo fijar la frecuencia {Metadata.DsdRateLabel} (código {setRateErr}).");
                }

                int numIn = 0, numOut = 0;
                if (CallGetChannels(&numIn, &numOut) != AseOk || numOut <= 0)
                {
                    throw new InvalidOperationException($"'{driver.Name}' no reportó canales de salida ASIO.");
                }

                _activeChannels = Math.Min(Metadata.Channels, numOut);

                int minSize = 0, maxSize = 0, preferredSize = 0, granularity = 0;
                if (CallGetBufferSize(&minSize, &maxSize, &preferredSize, &granularity) != AseOk || preferredSize < 8)
                {
                    throw new InvalidOperationException($"'{driver.Name}' devolvió un tamaño de buffer ASIO DSD inválido.");
                }

                var bufferBits = (preferredSize / 8) * 8;
                _bufferBytesPerChannel = Math.Max(1, bufferBits / 8);

                var chInfo = new AsioChannelInfo { Channel = 0, IsInput = 0 };
                var sampleType = AsioStDsdInt8Lsb1;
                if (CallGetChannelInfo(&chInfo) == AseOk)
                {
                    sampleType = chInfo.Type;
                }

                var driverIsLsbFirst = sampleType != AsioStDsdInt8Msb1;
                _reverseBits = Metadata.IsLsbFirst != driverIsLsbFirst;
                SampleTypeLabel = sampleType switch
                {
                    AsioStDsdInt8Lsb1 => "DSD LSB1",
                    AsioStDsdInt8Msb1 => "DSD MSB1",
                    _ => $"DSD type {sampleType}"
                };

                if (startPosition is { } pos && pos > TimeSpan.Zero)
                {
                    Seek(pos);
                }

                _bufferInfosPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AsioBufferInfo>() * _activeChannels);
                var infos = (AsioBufferInfo*)_bufferInfosPtr;
                for (var ch = 0; ch < _activeChannels; ch++)
                {
                    infos[ch] = new AsioBufferInfo
                    {
                        IsInput = 0,
                        ChannelNum = ch,
                        Buffer0 = IntPtr.Zero,
                        Buffer1 = IntPtr.Zero
                    };
                }

                _callbacksPtr = Marshal.AllocHGlobal(Marshal.SizeOf<AsioCallbacks>());
                var callbacks = (AsioCallbacks*)_callbacksPtr;
                callbacks->BufferSwitch = Marshal.GetFunctionPointerForDelegate(_bufferSwitchDelegate);
                callbacks->SampleRateDidChange = Marshal.GetFunctionPointerForDelegate(_sampleRateDidChangeDelegate);
                callbacks->AsioMessage = Marshal.GetFunctionPointerForDelegate(_asioMessageDelegate);
                callbacks->BufferSwitchTimeInfo = Marshal.GetFunctionPointerForDelegate(_bufferSwitchTimeInfoDelegate);

                var createErr = CallCreateBuffers(infos, _activeChannels, bufferBits, callbacks);
                if (createErr != AseOk)
                {
                    throw new InvalidOperationException($"'{driver.Name}' no pudo crear buffers ASIO DSD (código {createErr}).");
                }

                _buffersCreated = true;

                // Pre-fill all buffers with DSD silence (0x69) to guarantee click-free start
                for (var ch = 0; ch < _activeChannels; ch++)
                {
                    if (infos[ch].Buffer0 != IntPtr.Zero)
                    {
                        new Span<byte>((void*)infos[ch].Buffer0, _bufferBytesPerChannel).Fill(DsdSilenceByte);
                    }

                    if (infos[ch].Buffer1 != IntPtr.Zero)
                    {
                        new Span<byte>((void*)infos[ch].Buffer1, _bufferBytesPerChannel).Fill(DsdSilenceByte);
                    }
                }

                var startErr = CallStart();
                if (startErr != AseOk)
                {
                    throw new InvalidOperationException($"'{driver.Name}' no pudo iniciar la reproducción ASIO DSD (código {startErr}).");
                }

                _started = true;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public DsdStreamMetadata Metadata { get; }

        public int ActiveChannels => _activeChannels;

        public string SampleTypeLabel { get; } = "DSD";

        public bool IsPaused
        {
            get => Volatile.Read(ref _isPaused);
            set => Volatile.Write(ref _isPaused, value);
        }

        public TimeSpan Duration => Metadata.Duration;

        public TimeSpan Position
        {
            get
            {
                lock (_readLock)
                {
                    return TimeSpan.FromSeconds((_reader.CurrentByteIndex * 8.0) / Metadata.SampleRate);
                }
            }
        }

        public void Seek(TimeSpan position)
        {
            lock (_readLock)
            {
                var clamped = Math.Clamp(position.TotalSeconds, 0.0, Duration.TotalSeconds);
                var targetByte = (long)Math.Round((clamped * Metadata.SampleRate) / 8.0);
                _reader.SeekByte(targetByte);
                _endedSignaled = false;
            }
        }

        public static bool TryOpenControlPanel(Guid clsid, IntPtr windowHandle)
        {
            var iid = clsid;
            var hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out var ptr);
            if (hr < 0 || ptr == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var vtable = *(IntPtr**)ptr;
                var initFn = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)vtable[3];
                if (initFn(ptr, windowHandle) == 0)
                {
                    return false;
                }

                var panelFn = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtable[21];
                panelFn(ptr);
                return true;
            }
            finally
            {
                Marshal.Release(ptr);
            }
        }

        private void OnBufferSwitch(int doubleBufferIndex, int directProcess)
        {
            if (_disposed || _bufferInfosPtr == IntPtr.Zero)
            {
                return;
            }

            var infos = (AsioBufferInfo*)_bufferInfosPtr;
            var paused = Volatile.Read(ref _isPaused);
            var bytesPerChannel = _bufferBytesPerChannel;
            var channels = _activeChannels;

            if (paused)
            {
                for (var ch = 0; ch < channels; ch++)
                {
                    var dst = doubleBufferIndex == 0 ? infos[ch].Buffer0 : infos[ch].Buffer1;
                    if (dst != IntPtr.Zero)
                    {
                        new Span<byte>((void*)dst, bytesPerChannel).Fill(DsdSilenceByte);
                    }
                }

                CallOutputReady();
                return;
            }

            var reachedEnd = false;
            double energySum = 0;
            var energyCount = 0;

            lock (_readLock)
            {
                Span<byte> frameBytes = stackalloc byte[Metadata.Channels];
                var byteIndex = 0;
                while (byteIndex < bytesPerChannel)
                {
                    if (!_reader.ReadNextFrameBytes(frameBytes))
                    {
                        reachedEnd = true;
                        break;
                    }

                    for (var ch = 0; ch < channels; ch++)
                    {
                        var dst = (byte*)(doubleBufferIndex == 0 ? infos[ch].Buffer0 : infos[ch].Buffer1);
                        var raw = frameBytes[ch];
                        dst[byteIndex] = _reverseBits ? BitReverseTable[raw] : raw;

                        if ((byteIndex & 7) == 0)
                        {
                            var ones = BitOperations.PopCount(raw);
                            var centered = ((ones / 8.0) * 2.0) - 1.0;
                            energySum += centered * centered;
                            energyCount++;
                        }
                    }

                    byteIndex++;
                }

                if (byteIndex < bytesPerChannel)
                {
                    var remaining = bytesPerChannel - byteIndex;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        var dst = (byte*)(doubleBufferIndex == 0 ? infos[ch].Buffer0 : infos[ch].Buffer1);
                        new Span<byte>(dst + byteIndex, remaining).Fill(DsdSilenceByte);
                    }
                }
            }

            CallOutputReady();

            if (energyCount > 0)
            {
                _levelSink?.Invoke(Math.Clamp(Math.Sqrt(energySum / energyCount) * 1.45, 0.0, 1.0));
            }

            if (reachedEnd && !_endedSignaled)
            {
                _endedSignaled = true;
                Task.Run(_endedCallback);
            }
        }

        private void OnSampleRateDidChange(double sRate)
        {
        }

        private int OnAsioMessage(int selector, int value, IntPtr message, IntPtr opt)
        {
            return selector switch
            {
                1 => value is 1 or 2 or 3 or 4 or 5 or 7 ? 1 : 0, // kAsioSelectorSupported
                2 => 2, // kAsioEngineVersion
                3 => 1, // kAsioResetRequest
                4 => 1, // kAsioBufferSizeChange
                5 => 1, // kAsioResyncRequest
                7 => 0, // kAsioSupportsTimeInfo -> prefer direct bufferSwitch
                _ => 0
            };
        }

        private IntPtr OnBufferSwitchTimeInfo(IntPtr timeInfo, int doubleBufferIndex, int directProcess)
        {
            OnBufferSwitch(doubleBufferIndex, directProcess);
            return timeInfo;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_asioPtr != IntPtr.Zero && _vtable is not null)
            {
                if (_started)
                {
                    try { CallStop(); } catch { }
                    _started = false;
                }

                if (_buffersCreated)
                {
                    try { CallDisposeBuffers(); } catch { }
                    _buffersCreated = false;
                }

                if (_switchedToDsd)
                {
                    try
                    {
                        var pcmFormat = new AsioIoFormat { FormatType = KAsioPcmFormat };
                        CallFuture(KAsioSetIoFormat, &pcmFormat);
                    }
                    catch
                    {
                    }

                    _switchedToDsd = false;
                }

                try { Marshal.Release(_asioPtr); } catch { }
                _asioPtr = IntPtr.Zero;
                _vtable = null;
            }

            if (_bufferInfosPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_bufferInfosPtr);
                _bufferInfosPtr = IntPtr.Zero;
            }

            if (_callbacksPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_callbacksPtr);
                _callbacksPtr = IntPtr.Zero;
            }

            _reader.Dispose();
        }

        private int CallInit(IntPtr sysHandle) => ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)_vtable[3])(_asioPtr, sysHandle);
        private int CallStart() => ((delegate* unmanaged[Stdcall]<IntPtr, int>)_vtable[7])(_asioPtr);
        private int CallStop() => ((delegate* unmanaged[Stdcall]<IntPtr, int>)_vtable[8])(_asioPtr);
        private int CallGetChannels(int* numIn, int* numOut) => ((delegate* unmanaged[Stdcall]<IntPtr, int*, int*, int>)_vtable[9])(_asioPtr, numIn, numOut);
        private int CallGetBufferSize(int* min, int* max, int* pref, int* gran) => ((delegate* unmanaged[Stdcall]<IntPtr, int*, int*, int*, int*, int>)_vtable[11])(_asioPtr, min, max, pref, gran);
        private int CallCanSampleRate(double rate) => ((delegate* unmanaged[Stdcall]<IntPtr, double, int>)_vtable[12])(_asioPtr, rate);
        private int CallSetSampleRate(double rate) => ((delegate* unmanaged[Stdcall]<IntPtr, double, int>)_vtable[14])(_asioPtr, rate);
        private int CallGetChannelInfo(AsioChannelInfo* info) => ((delegate* unmanaged[Stdcall]<IntPtr, AsioChannelInfo*, int>)_vtable[18])(_asioPtr, info);
        private int CallCreateBuffers(AsioBufferInfo* infos, int numCh, int bufSize, AsioCallbacks* callbacks) => ((delegate* unmanaged[Stdcall]<IntPtr, AsioBufferInfo*, int, int, AsioCallbacks*, int>)_vtable[19])(_asioPtr, infos, numCh, bufSize, callbacks);
        private int CallDisposeBuffers() => ((delegate* unmanaged[Stdcall]<IntPtr, int>)_vtable[20])(_asioPtr);
        private int CallFuture(int selector, void* opt) => ((delegate* unmanaged[Stdcall]<IntPtr, int, void*, int>)_vtable[22])(_asioPtr, selector, opt);
        private int CallOutputReady() => ((delegate* unmanaged[Stdcall]<IntPtr, int>)_vtable[23])(_asioPtr);

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

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(
            ref Guid rclsid,
            IntPtr pUnkOuter,
            uint dwClsContext,
            ref Guid riid,
            out IntPtr ppv);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void BufferSwitchDelegate(int doubleBufferIndex, int directProcess);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SampleRateDidChangeDelegate(double sRate);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int AsioMessageDelegate(int selector, int value, IntPtr message, IntPtr opt);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr BufferSwitchTimeInfoDelegate(IntPtr timeInfo, int doubleBufferIndex, int directProcess);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct AsioIoFormat
        {
            public int FormatType;
            public fixed byte Future[508];
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct AsioBufferInfo
        {
            public int IsInput;
            public int ChannelNum;
            public IntPtr Buffer0;
            public IntPtr Buffer1;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct AsioChannelInfo
        {
            public int Channel;
            public int IsInput;
            public int IsActive;
            public int ChannelGroup;
            public int Type;
            public fixed byte Name[32];
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct AsioCallbacks
        {
            public IntPtr BufferSwitch;
            public IntPtr SampleRateDidChange;
            public IntPtr AsioMessage;
            public IntPtr BufferSwitchTimeInfo;
        }
    }
}
