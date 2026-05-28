using System.Runtime.InteropServices;
using System.Globalization;

namespace ZenithAudio.Core.Audio;

public sealed class AudioEngine : IDisposable
{
    private AudioEngineOptions _options = new();
    private BassWasapiSession? _bassSession;
    private MpvSession? _mpvSession;
    private bool _initialized;

    static AudioEngine()
    {
        NativeLibrary.SetDllImportResolver(typeof(AudioEngine).Assembly, ResolveNativeLibrary);
    }

    public event EventHandler<PlaybackState>? PlaybackStateChanged;

    public event EventHandler<AudioSignalInfo>? SignalChanged;

    public Task InitializeAsync(AudioEngineOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Stop();
        _options = options;
        RaiseState(PlaybackState.Initializing);

        if (options.Backend == AudioBackend.MpvWasapi)
        {
            _mpvSession = new MpvSession(options);
            _mpvSession.Initialize();
        }
        else
        {
            _bassSession = new BassWasapiSession(options, RaiseSignal);
            _bassSession.Initialize();
        }

        _initialized = true;
        RaiseState(PlaybackState.Ready);
        return Task.CompletedTask;
    }

    public Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_initialized)
        {
            return InitializeAsync(_options, cancellationToken).ContinueWith(_ => PlayAsync(filePath, cancellationToken), cancellationToken).Unwrap();
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected audio file does not exist.", filePath);
        }

        var signal = AudioSignalInspector.FromFileName(filePath);
        RaiseSignal(signal);

        if (_options.Backend == AudioBackend.MpvWasapi)
        {
            _mpvSession ??= new MpvSession(_options);
            _mpvSession.Initialize();
            _mpvSession.Play(filePath);
        }
        else
        {
            _bassSession ??= new BassWasapiSession(_options, RaiseSignal);
            _bassSession.Initialize();
            _bassSession.Play(filePath);
        }

        RaiseState(PlaybackState.Playing);
        return Task.CompletedTask;
    }

    public void Pause()
    {
        if (_options.Backend == AudioBackend.MpvWasapi)
        {
            _mpvSession?.Pause();
        }
        else
        {
            _bassSession?.Pause();
        }

        RaiseState(PlaybackState.Paused);
    }

    public void Resume()
    {
        if (_options.Backend == AudioBackend.MpvWasapi)
        {
            _mpvSession?.Resume();
        }
        else
        {
            _bassSession?.Resume();
        }

        RaiseState(PlaybackState.Playing);
    }

    public void Seek(TimeSpan position)
    {
        if (_options.Backend == AudioBackend.MpvWasapi)
        {
            _mpvSession?.Seek(position);
        }
        else
        {
            _bassSession?.Seek(position);
        }
    }

    public void Stop()
    {
        _bassSession?.Dispose();
        _bassSession = null;

        _mpvSession?.Dispose();
        _mpvSession = null;

        _initialized = false;
        RaiseState(PlaybackState.Stopped);
    }

    public void Dispose()
    {
        Stop();
    }

    private void RaiseState(PlaybackState state)
    {
        PlaybackStateChanged?.Invoke(this, state);
    }

    private void RaiseSignal(AudioSignalInfo signal)
    {
        SignalChanged?.Invoke(this, signal);
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        var path = FindNativeLibraryPath(libraryName);
        if (path is not null && NativeLibrary.TryLoad(path, out var handle))
        {
            return handle;
        }

        return IntPtr.Zero;
    }

    private static string? FindNativeLibraryPath(string libraryName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, libraryName),
            Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", libraryName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed class BassWasapiSession : IDisposable
    {
        private const uint BassUnicode = 0x80000000;
        private const uint BassStreamDecode = 0x00200000;
        private const uint BassSampleFloat = 0x00000100;
        private const uint BassWasapiExclusive = 0x00000001;
        private const uint BassWasapiEvent = 0x00000010;

        private readonly AudioEngineOptions _options;
        private readonly Action<AudioSignalInfo> _signalSink;
        private readonly WasapiProc _wasapiProc;
        private IntPtr _stream;
        private bool _bassReady;
        private bool _wasapiReady;

        public BassWasapiSession(AudioEngineOptions options, Action<AudioSignalInfo> signalSink)
        {
            _options = options;
            _signalSink = signalSink;
            _wasapiProc = WasapiCallback;
        }

        public void Initialize()
        {
            if (_bassReady)
            {
                return;
            }

            if (!BassNative.BASS_Init(0, 44100, 0, IntPtr.Zero, IntPtr.Zero))
            {
                ThrowBass("BASS_Init failed");
            }

            LoadBassPlugin("bassdsd.dll");
            LoadBassPlugin("bass_ape.dll");
            LoadBassPlugin("bass_wv.dll");
            LoadBassPlugin("bassopus.dll");
            _bassReady = true;
        }

        public void Play(string filePath)
        {
            FreeStream();

            _stream = CreateDecodeStream(filePath);
            if (_stream == IntPtr.Zero)
            {
                ThrowBass("Could not create a BASS decode stream. Check that bassdsd.dll is available for DSF/DFF files");
            }

            var info = BassChannelInfo.Empty;
            if (!BassNative.BASS_ChannelGetInfo(_stream, ref info))
            {
                ThrowBass("Could not read BASS channel info");
            }

            var sampleRate = info.Frequency > 0 ? info.Frequency : 44100;
            var channels = info.Channels > 0 ? info.Channels : 2;
            var flags = _options.UseWasapiExclusive ? BassWasapiExclusive | BassWasapiEvent : BassWasapiEvent;
            var bufferSeconds = Math.Clamp(_options.BufferMilliseconds, 50, 500) / 1000f;

            var deviceIndex = ResolveWasapiDeviceIndex(_options);
            if (!BassWasapiNative.BASS_WASAPI_Init(deviceIndex, sampleRate, channels, flags, bufferSeconds, 0.0f, _wasapiProc, IntPtr.Zero))
            {
                ThrowWasapi("BASS_WASAPI_Init failed. The output device may not support this sample rate in exclusive mode");
            }

            _wasapiReady = true;

            if (!BassWasapiNative.BASS_WASAPI_Start())
            {
                ThrowWasapi("BASS_WASAPI_Start failed");
            }

            var inferred = AudioSignalInspector.FromFileName(filePath);
            _signalSink(inferred with
            {
                SampleRate = sampleRate,
                Channels = channels
            });
        }

        public void Dispose()
        {
            if (_wasapiReady)
            {
                BassWasapiNative.BASS_WASAPI_Stop(true);
                BassWasapiNative.BASS_WASAPI_Free();
                _wasapiReady = false;
            }

            FreeStream();

            if (_bassReady)
            {
                BassNative.BASS_Free();
                _bassReady = false;
            }
        }

        public void Pause()
        {
            if (_wasapiReady)
            {
                BassWasapiNative.BASS_WASAPI_Stop(false);
            }
        }

        public void Resume()
        {
            if (_wasapiReady && !BassWasapiNative.BASS_WASAPI_Start())
            {
                ThrowWasapi("BASS_WASAPI_Start failed");
            }
        }

        public void Seek(TimeSpan position)
        {
            if (_stream == IntPtr.Zero)
            {
                return;
            }

            var bytePosition = BassNative.BASS_ChannelSeconds2Bytes(_stream, Math.Max(position.TotalSeconds, 0));
            if (bytePosition >= 0)
            {
                BassNative.BASS_ChannelSetPosition(_stream, (ulong)bytePosition, 0);
            }
        }

        private IntPtr CreateDecodeStream(string filePath)
        {
            var flags = BassStreamDecode | BassSampleFloat | BassUnicode;
            var extension = Path.GetExtension(filePath);

            if (extension.Equals(".dsf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".dff", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var dsdStream = BassDsdNative.BASS_DSD_StreamCreateFile(false, filePath, 0, 0, flags, 0);
                    if (dsdStream != IntPtr.Zero)
                    {
                        return dsdStream;
                    }
                }
                catch (DllNotFoundException)
                {
                    return IntPtr.Zero;
                }
            }

            return BassNative.BASS_StreamCreateFile(false, filePath, 0, 0, flags);
        }

        private static void LoadBassPlugin(string fileName)
        {
            var pluginPath = FindNativeLibraryPath(fileName) ?? fileName;
            BassNative.BASS_PluginLoad(pluginPath, 0);
        }

        private static int ResolveWasapiDeviceIndex(AudioEngineOptions options)
        {
            if (options.DeviceIndex >= 0)
            {
                return options.DeviceIndex;
            }

            if (string.IsNullOrWhiteSpace(options.DeviceName) && string.IsNullOrWhiteSpace(options.DeviceId))
            {
                return -1;
            }

            for (var index = 0; index < 128; index++)
            {
                var info = BassWasapiDeviceInfo.Empty;
                if (!BassWasapiNative.BASS_WASAPI_GetDeviceInfo(index, ref info))
                {
                    continue;
                }

                var name = Marshal.PtrToStringAnsi(info.Name) ?? string.Empty;
                var id = Marshal.PtrToStringAnsi(info.Id) ?? string.Empty;
                var isInput = (info.Flags & BassWasapiDeviceInfo.Input) != 0;
                var isLoopback = (info.Flags & BassWasapiDeviceInfo.Loopback) != 0;
                var isEnabled = (info.Flags & BassWasapiDeviceInfo.Enabled) != 0;
                if (isInput || isLoopback || !isEnabled)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(options.DeviceId) &&
                    id.Contains(options.DeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }

                if (!string.IsNullOrWhiteSpace(options.DeviceName) &&
                    name.Contains(options.DeviceName, StringComparison.CurrentCultureIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private int WasapiCallback(IntPtr buffer, int length, IntPtr user)
        {
            if (_stream == IntPtr.Zero)
            {
                return 0;
            }

            var read = BassNative.BASS_ChannelGetData(_stream, buffer, length);
            return read < 0 ? 0 : read;
        }

        private void FreeStream()
        {
            if (_stream != IntPtr.Zero)
            {
                BassNative.BASS_StreamFree(_stream);
                _stream = IntPtr.Zero;
            }
        }

        private static void ThrowBass(string message)
        {
            throw new InvalidOperationException($"{message}. BASS error: {BassNative.BASS_ErrorGetCode()}");
        }

        private static void ThrowWasapi(string message)
        {
            throw new InvalidOperationException($"{message}. BASS WASAPI error: {BassNative.BASS_ErrorGetCode()}");
        }
    }

    private sealed class MpvSession : IDisposable
    {
        private readonly AudioEngineOptions _options;
        private IntPtr _handle;
        private bool _initialized;

        public MpvSession(AudioEngineOptions options)
        {
            _options = options;
        }

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _handle = MpvNative.mpv_create();
            if (_handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("mpv_create failed. Check that mpv-2.dll is available.");
            }

            SetOption("ao", "wasapi");
            SetOption("audio-exclusive", _options.UseWasapiExclusive ? "yes" : "no");
            SetOption("audio-buffer", $"{Math.Clamp(_options.BufferMilliseconds, 50, 500) / 1000.0:0.###}");
            SetOption("terminal", "no");

            var result = MpvNative.mpv_initialize(_handle);
            if (result < 0)
            {
                throw new InvalidOperationException($"mpv_initialize failed: {MpvNative.GetError(result)}");
            }

            _initialized = true;
        }

        public void Play(string filePath)
        {
            Initialize();
            var escaped = filePath.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
            var result = MpvNative.mpv_command_string(_handle, $"loadfile \"{escaped}\" replace");
            if (result < 0)
            {
                throw new InvalidOperationException($"MPV could not load the file: {MpvNative.GetError(result)}");
            }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                MpvNative.mpv_terminate_destroy(_handle);
                _handle = IntPtr.Zero;
                _initialized = false;
            }
        }

        public void Pause()
        {
            SetPause(true);
        }

        public void Resume()
        {
            SetPause(false);
        }

        public void Seek(TimeSpan position)
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            var seconds = Math.Max(position.TotalSeconds, 0).ToString("0.###", CultureInfo.InvariantCulture);
            var result = MpvNative.mpv_command_string(_handle, $"seek {seconds} absolute exact");
            if (result < 0)
            {
                throw new InvalidOperationException($"MPV seek failed: {MpvNative.GetError(result)}");
            }
        }

        private void SetOption(string name, string value)
        {
            var result = MpvNative.mpv_set_option_string(_handle, name, value);
            if (result < 0)
            {
                throw new InvalidOperationException($"MPV option '{name}' failed: {MpvNative.GetError(result)}");
            }
        }

        private void SetPause(bool paused)
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            var result = MpvNative.mpv_set_property_string(_handle, "pause", paused ? "yes" : "no");
            if (result < 0)
            {
                throw new InvalidOperationException($"MPV pause failed: {MpvNative.GetError(result)}");
            }
        }
    }

    private static class AudioSignalInspector
    {
        public static AudioSignalInfo FromFileName(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            return extension switch
            {
                ".dsf" or ".dff" => new AudioSignalInfo(2822400, 1, 2, 5645, true, "DSD"),
                ".flac" => new AudioSignalInfo(0, 24, 0, 0, false, "FLAC"),
                ".wav" => new AudioSignalInfo(0, 24, 0, 0, false, "PCM"),
                ".aiff" or ".aif" => new AudioSignalInfo(0, 24, 0, 0, false, "AIFF"),
                ".ape" => new AudioSignalInfo(0, 16, 0, 0, false, "Monkey's Audio"),
                ".wv" => new AudioSignalInfo(0, 24, 0, 0, false, "WavPack"),
                ".opus" => new AudioSignalInfo(0, 0, 0, 0, false, "Opus"),
                _ => new AudioSignalInfo(0, 0, 0, 0, false, extension.TrimStart('.').ToUpperInvariant())
            };
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int WasapiProc(IntPtr buffer, int length, IntPtr user);

    [StructLayout(LayoutKind.Sequential)]
    private struct BassChannelInfo
    {
        public int Frequency;
        public int Channels;
        public uint Flags;
        public uint ChannelType;
        public uint OriginalResolution;
        public IntPtr Plugin;
        public IntPtr Sample;
        public IntPtr FileName;

        public static BassChannelInfo Empty => new();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BassWasapiDeviceInfo
    {
        public const uint Enabled = 1;
        public const uint Loopback = 2;
        public const uint Input = 4;

        public IntPtr Name;
        public IntPtr Id;
        public uint Type;
        public uint Flags;
        public float MinPeriod;
        public float DefaultPeriod;
        public int MixFrequency;
        public int MixChannels;

        public static BassWasapiDeviceInfo Empty => new();
    }

    private static class BassNative
    {
        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BASS_Init(int device, int frequency, uint flags, IntPtr windowHandle, IntPtr clsid);

        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BASS_Free();

        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int BASS_ErrorGetCode();

        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern IntPtr BASS_PluginLoad(string fileName, uint flags);

        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern IntPtr BASS_StreamCreateFile(
            [MarshalAs(UnmanagedType.Bool)] bool memory,
            string file,
            ulong offset,
            ulong length,
            uint flags);

        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BASS_StreamFree(IntPtr handle);

        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int BASS_ChannelGetData(IntPtr handle, IntPtr buffer, int length);

        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BASS_ChannelGetInfo(IntPtr handle, ref BassChannelInfo info);

        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern long BASS_ChannelSeconds2Bytes(IntPtr handle, double position);

        [DllImport("bass.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BASS_ChannelSetPosition(IntPtr handle, ulong position, uint mode);
    }

    private static class BassDsdNative
    {
        [DllImport("bassdsd.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern IntPtr BASS_DSD_StreamCreateFile(
            [MarshalAs(UnmanagedType.Bool)] bool memory,
            string file,
            ulong offset,
            ulong length,
            uint flags,
            uint frequency);
    }

    private static class BassWasapiNative
    {
        [DllImport("basswasapi.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BASS_WASAPI_Init(
            int device,
            int frequency,
            int channels,
            uint flags,
            float buffer,
            float period,
            WasapiProc callback,
            IntPtr user);

        [DllImport("basswasapi.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BASS_WASAPI_Start();

        [DllImport("basswasapi.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BASS_WASAPI_Stop([MarshalAs(UnmanagedType.Bool)] bool reset);

        [DllImport("basswasapi.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BASS_WASAPI_Free();

        [DllImport("basswasapi.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BASS_WASAPI_GetDeviceInfo(int device, ref BassWasapiDeviceInfo info);
    }

    private static class MpvNative
    {
        [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_create();

        [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_initialize(IntPtr handle);

        [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

        [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string args);

        [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_terminate_destroy(IntPtr handle);

        [DllImport("mpv-2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr mpv_error_string(int error);

        public static string GetError(int error)
        {
            var pointer = mpv_error_string(error);
            return pointer == IntPtr.Zero ? error.ToString() : Marshal.PtrToStringUTF8(pointer) ?? error.ToString();
        }
    }
}
