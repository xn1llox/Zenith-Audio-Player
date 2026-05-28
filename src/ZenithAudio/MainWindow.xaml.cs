using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Devices.Enumeration;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Pickers;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT.Interop;
using ZenithAudio.Core.Ai;
using ZenithAudio.Core.Audio;

namespace ZenithAudio;

public sealed partial class MainWindow : Window
{
    private static readonly HashSet<string> SupportedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dsf",
        ".dff",
        ".iso",
        ".flac",
        ".wav",
        ".aiff",
        ".aif",
        ".alac",
        ".mqa",
        ".ape",
        ".wv",
        ".mp3",
        ".aac",
        ".ogg",
        ".opus",
        ".cue"
    };

    private static readonly HashSet<string> DsdExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dsf",
        ".dff",
        ".iso"
    };

    private static readonly HashSet<string> HiResPcmExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".flac",
        ".wav",
        ".aiff",
        ".aif",
        ".alac",
        ".mqa",
        ".ape",
        ".wv"
    };

    private static readonly HashSet<string> WindowsFallbackExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".flac",
        ".wav",
        ".aiff",
        ".aif",
        ".mp3",
        ".aac",
        ".ogg",
        ".opus"
    };

    private readonly AudioEngine _audioEngine = new();
    private readonly AudioLevelAnalyzer _audioLevelAnalyzer = new();
    private readonly ToneControlSettings _toneSettings = new();
    private readonly ZenithAiClient _zenithAiClient = new();
    private readonly List<ZenithAiChatMessage> _zenithAiMessages = [];
    private readonly List<string> _libraryFolders = [];
    private readonly List<LibraryTrack> _libraryTracks = [];
    private readonly List<LibraryTrack> _visibleLibraryTracks = [];
    private readonly Dictionary<string, IsoAudioEntry> _isoEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CueAudioEntry> _cueEntries = new(StringComparer.OrdinalIgnoreCase);
    private string _activeLibraryTitle = "Explorador de biblioteca";
    private readonly List<OutputDeviceOption> _outputDevices = [];
    private List<SyncedLyricLine> _syncedLyrics = [];
    private int _currentLyricIndex = -1;
    private string? _currentFilePath;
    private bool _isPaused;
    private bool _usingFallbackPlayer;
    private bool _isSeeking;
    private bool _isUpdatingPlaybackProgress;
    private string? _temporaryFallbackFilePath;
    private readonly MediaPlayer _fallbackMediaPlayer = new();
    private readonly DispatcherQueueTimer _playbackTimer;
    private readonly DispatcherQueueTimer _vuTimer;
    private double _vuTargetLevel;
    private double _vuSmoothedTargetLevel;
    private double _vuDisplayLevel;
    private double _vuPhase;
    private bool _vuUsesLiveLevel;
    private bool _vuUsesAnalyzer;
    private readonly Random _shuffleRandom = new();
    private bool _shuffleEnabled = true;
    private bool _isApplyingAdaptiveBuffer;
    private string _bufferProfile = "manual";
    private CueAudioEntry? _pendingCueEntry;

    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        SetWindowIcon();
        _playbackTimer = DispatcherQueue.CreateTimer();
        _playbackTimer.Interval = TimeSpan.FromMilliseconds(500);
        _playbackTimer.Tick += PlaybackTimer_Tick;
        _vuTimer = DispatcherQueue.CreateTimer();
        _vuTimer.Interval = TimeSpan.FromMilliseconds(16);
        _vuTimer.Tick += VuTimer_Tick;
        RefreshBackendLabels();
        LibraryBrowserListView.ItemsSource = Array.Empty<object>();
        LibraryFoldersListView.ItemsSource = _libraryFolders;
        AutoEqSearchBox.ItemsSource = new[]
        {
            "KZ Carol Pro",
            "Moondrop Chu II",
            "Moondrop Aria",
            "Sennheiser HD 600",
            "Sennheiser IE 200",
            "Sony WH-1000XM5",
            "Sony IER-M9"
        };
        AutoEqSearchBox.QuerySubmitted += AutoEqSearchBox_QuerySubmitted;

        _audioEngine.PlaybackStateChanged += AudioEngine_PlaybackStateChanged;
        _audioEngine.SignalChanged += AudioEngine_SignalChanged;
        FallbackPlayerElement.SetMediaPlayer(_fallbackMediaPlayer);
        _fallbackMediaPlayer.MediaOpened += FallbackMediaPlayer_MediaOpened;
        _fallbackMediaPlayer.MediaEnded += FallbackMediaPlayer_MediaEnded;
        _fallbackMediaPlayer.AudioCategory = MediaPlayerAudioCategory.Media;
        _fallbackMediaPlayer.Volume = 1.0;
        Closed += MainWindow_Closed;
        InitializeOutputDeviceList();
        UpdateSacdExtractorStatus();
        UpdateToneControlState(showStatus: false);
        ApplyOutputDeviceSettings();
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
        _vuUsesLiveLevel = false;
        ResetVuMeter();
        UpdateShuffleButtonVisual();
        _ = RefreshOutputDevicesAsync();
    }

    private void SetWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icono.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
    }

    public async Task OpenAndPlayFileAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        var extension = Path.GetExtension(filePath);
        if (!SupportedAudioExtensions.Contains(extension))
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = $"Formato no soportado para apertura directa: {extension}";
            return;
        }

        StopCurrentPlaybackForTrackChange();

        if (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
        {
            await OpenIsoImageAsync(filePath);
            var firstTrack = _visibleLibraryTracks.FirstOrDefault();
            if (firstTrack is not null && !firstTrack.Extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                LoadTrack(firstTrack.Path);
                await Task.Delay(80);
                PlayButton_Click(this, null!);
            }

            return;
        }

        if (extension.Equals(".cue", StringComparison.OrdinalIgnoreCase))
        {
            await OpenCueSheetAsync(filePath);
            return;
        }

        LoadTrack(filePath);
        await Task.Delay(80);
        PlayButton_Click(this, null!);
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary
        };

        picker.FileTypeFilter.Add(".dsf");
        picker.FileTypeFilter.Add(".dff");
        picker.FileTypeFilter.Add(".flac");
        picker.FileTypeFilter.Add(".wav");
        picker.FileTypeFilter.Add(".aiff");
        picker.FileTypeFilter.Add(".alac");
        picker.FileTypeFilter.Add(".mqa");
        picker.FileTypeFilter.Add(".ape");
        picker.FileTypeFilter.Add(".wv");
        picker.FileTypeFilter.Add(".opus");
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".aac");
        picker.FileTypeFilter.Add(".ogg");
        picker.FileTypeFilter.Add(".iso");
        picker.FileTypeFilter.Add(".cue");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        _currentFilePath = file.Path;
        if (Path.GetExtension(file.Path).Equals(".iso", StringComparison.OrdinalIgnoreCase))
        {
            await OpenIsoImageAsync(file.Path);
            return;
        }

        if (Path.GetExtension(file.Path).Equals(".cue", StringComparison.OrdinalIgnoreCase))
        {
            await OpenCueSheetAsync(file.Path);
            return;
        }

        TrackTitleTextBlock.Text = file.Name;
        TrackPathTextBlock.Text = file.Path;
        CodecTextBlock.Text = Path.GetExtension(file.Path).TrimStart('.').ToUpperInvariant();
        TransportBadgeTextBlock.Text = "CARGADO";
        UpdateNowPlayingVisuals(file.Path);
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath is null)
        {
            return;
        }

        var requestedFilePath = _currentFilePath;
        var playbackFilePath = await ResolvePlayablePathAsync(requestedFilePath);
        if (playbackFilePath is null)
        {
            return;
        }

        var backend = GetSelectedBackend();
        BackendTextBlock.Text = backend == AudioBackend.BassWasapi ? "BASS" : "MPV";
        ApplyAdaptiveBufferForTrack(playbackFilePath);
        OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn ? "DAC exclusivo" : "Windows compartido";
        PlayButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = "Preparando reproducción";

        try
        {
            var extension = Path.GetExtension(playbackFilePath);
            var isDsd = DsdExtensions.Contains(extension);
            var nativeAvailable = IsNativeBackendAvailable(backend, playbackFilePath);
            var mpvWithoutExclusiveDac = backend == AudioBackend.MpvWasapi && !IsExclusiveDacSelected();

            if (nativeAvailable)
            {
                if (mpvWithoutExclusiveDac)
                {
                    ApplyWindowsMaximumQualityFallback(backend, "MPV seleccionado sin DAC exclusivo");
                }

                var selectedOutputDevice = GetSelectedOutputDevice();
                var hasSpecificOutputDevice = selectedOutputDevice is not null && !selectedOutputDevice.IsSystemDefault;
                var selectedDeviceName = hasSpecificOutputDevice ? selectedOutputDevice!.Name : null;
                var selectedDeviceId = hasSpecificOutputDevice ? selectedOutputDevice!.Id : null;

                await _audioEngine.InitializeAsync(new AudioEngineOptions
                {
                    Backend = backend,
                    BufferMilliseconds = (int)BufferSlider.Value,
                    DeviceName = selectedDeviceName,
                    DeviceId = selectedDeviceId,
                    UseWasapiExclusive = ExclusiveModeToggle.IsOn && !mpvWithoutExclusiveDac
                });

                await _audioEngine.PlayAsync(playbackFilePath);
                if (_pendingCueEntry is not null && _pendingCueEntry.Start > TimeSpan.Zero)
                {
                    _audioEngine.Seek(_pendingCueEntry.Start);
                }

                _usingFallbackPlayer = false;
                _vuUsesLiveLevel = false;
                PrepareVuAnalyzer(playbackFilePath);
                CleanupTemporaryFallbackFile();
                _playbackTimer.Start();
                StartVuMeter();
                OutputPathTextBlock.Text = mpvWithoutExclusiveDac
                    ? "MPV compartido max"
                    : ExclusiveModeToggle.IsOn ? "DAC exclusivo" : "Windows compartido";
                UpdateSignalChain("Decodificación nativa");
                WritePlaybackAuditLog(playbackFilePath, "Nativo");
            }
            else
            {
                if (isDsd)
                {
                    ApplyWindowsMaximumQualityFallback(backend, "DSD nativo no disponible");
                    await PlayWithWindowsFallbackAsync(playbackFilePath);
                }
                else
                {
                    ApplyWindowsMaximumQualityFallback(backend, "Backend nativo no disponible");
                    await PlayWithWindowsFallbackAsync(playbackFilePath);
                }
            }

            PlayButton.IsEnabled = true;
            PauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            _isPaused = false;
            TransportBadgeTextBlock.Text = "REPRODUCIENDO";
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Message = _usingFallbackPlayer
                ? "Reproduciendo con Windows fallback a la mayor calidad negociada por el sistema"
                : ExclusiveModeToggle.IsOn && !mpvWithoutExclusiveDac ? "Reproduciendo en modo exclusivo" : "Reproduciendo en modo compartido";
        }
        catch (Exception ex)
        {
            UpdatePlaybackAvailabilityV2();
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            TransportBadgeTextBlock.Text = "ERROR";
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = ex.Message;
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_usingFallbackPlayer)
        {
            _fallbackMediaPlayer.Pause();
            _fallbackMediaPlayer.Source = null;
            CleanupTemporaryFallbackFile();
            _playbackTimer.Stop();
            _vuTimer.Stop();
            PlaybackProgressSlider.Value = 0;
            CurrentTimeTextBlock.Text = "0:00";
            DurationTextBlock.Text = "0:00";
        }
        else
        {
            _audioEngine.Stop();
            _playbackTimer.Stop();
            _vuTimer.Stop();
        }

        _vuUsesAnalyzer = false;
        _vuUsesLiveLevel = false;
        ResetVuMeter();
        _isPaused = false;
        StopButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        TransportBadgeTextBlock.Text = "DETENIDO";
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = "Detenido";
        UpdateSignalChain("Detenido");
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isPaused)
        {
            if (_usingFallbackPlayer)
            {
                _fallbackMediaPlayer.Pause();
            }
            else
            {
                _audioEngine.Pause();
            }

            _isPaused = true;
            TransportBadgeTextBlock.Text = "PAUSADO";
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = "Pausado";
            _vuTimer.Stop();
            ResetVuMeter();
            return;
        }

        if (_usingFallbackPlayer)
        {
            _fallbackMediaPlayer.Play();
        }
        else
        {
            _audioEngine.Resume();
        }

        _isPaused = false;
        TransportBadgeTextBlock.Text = "REPRODUCIENDO";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.Message = "Reproduciendo";
        _playbackTimer.Start();
        StartVuMeter();
    }

    private void ShuffleButton_Click(object sender, RoutedEventArgs e)
    {
        _shuffleEnabled = !_shuffleEnabled;
        UpdateShuffleButtonVisual();
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = _shuffleEnabled
            ? "Reproduccion aleatoria activada"
            : "Reproduccion secuencial activada";
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await PlayNextTrackAsync(manual: true);
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        await PlayPreviousTrackAsync(manual: true);
    }

    private void BufferSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_isApplyingAdaptiveBuffer)
        {
            _bufferProfile = "manual";
        }

        if (BufferTextBlock is not null)
        {
            BufferTextBlock.Text = $"{(int)e.NewValue} ms";
        }

        UpdateSignalChain();
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (VolumeTextBlock is null)
        {
            return;
        }

        var volume = Math.Clamp(e.NewValue / 100.0, 0.0, 1.0);
        _fallbackMediaPlayer.Volume = volume;
        VolumeTextBlock.Text = $"Volumen {(int)e.NewValue}%";
    }

    private void BackendComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackendTextBlock is null)
        {
            return;
        }

        var backend = GetSelectedBackend();
        BackendTextBlock.Text = backend == AudioBackend.BassWasapi ? "BASS" : "MPV";

        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
    }

    private void OutputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusInfoBar is null || OutputPathTextBlock is null)
        {
            return;
        }

        ApplyOutputDeviceSettings();
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
    }

    private void InitializeOutputDeviceList()
    {
        _outputDevices.Clear();
        _outputDevices.Add(OutputDeviceOption.SystemDefault);
        OutputDeviceComboBox.ItemsSource = null;
        OutputDeviceComboBox.ItemsSource = _outputDevices;
        OutputDeviceComboBox.SelectedIndex = 0;
    }

    private async Task RefreshOutputDevicesAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.AudioRender);

            DispatcherQueue.TryEnqueue(() =>
            {
                var selectedId = GetSelectedOutputDevice()?.Id;
                _outputDevices.Clear();
                _outputDevices.Add(OutputDeviceOption.SystemDefault);

                foreach (var device in devices.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    _outputDevices.Add(OutputDeviceOption.FromDevice(device));
                }

                OutputDeviceComboBox.ItemsSource = null;
                OutputDeviceComboBox.ItemsSource = _outputDevices;

                var selectedIndex = _outputDevices.FindIndex(device => !string.IsNullOrWhiteSpace(selectedId) && device.Id == selectedId);
                OutputDeviceComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;

                StatusInfoBar.Severity = InfoBarSeverity.Informational;
                StatusInfoBar.Message = devices.Count > 0
                    ? $"Dispositivos de salida detectados: {devices.Count}. Elige Realtek, USB o DAC desde System Settings."
                    : "Windows no reporto dispositivos de salida. Se usara el dispositivo predeterminado del sistema.";

                ApplyOutputDeviceSettings();
                UpdatePlaybackAvailabilityV2();
                UpdateSignalChain();
            });
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                StatusInfoBar.Severity = InfoBarSeverity.Warning;
                StatusInfoBar.Message = $"No se pudieron leer los dispositivos de salida de Windows: {ex.Message}";
            });
        }
    }

    private async void RefreshOutputDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = "Buscando dispositivos de salida instalados en Windows...";
        await RefreshOutputDevicesAsync();
    }

    private async void ZenithAiButton_Click(object sender, RoutedEventArgs e)
    {
        var dialogWidth = Math.Clamp(AppWindow.Size.Width - 180, 420, 760);
        var transcriptHeight = Math.Clamp(AppWindow.Size.Height - 390, 280, 520);
        var glassBrush = new AcrylicBrush
        {
            TintColor = Windows.UI.Color.FromArgb(255, 18, 30, 42),
            TintOpacity = 0.72,
            TintLuminosityOpacity = 0.78,
            FallbackColor = Windows.UI.Color.FromArgb(255, 24, 28, 36)
        };

        var transcriptTextBlock = new TextBlock
        {
            Text = FormatZenithAiTranscript(),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Width = dialogWidth - 42,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var transcriptScrollViewer = new ScrollViewer
        {
            Content = transcriptTextBlock,
            Height = transcriptHeight,
            Width = dialogWidth,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var questionTextBox = new TextBox
        {
            PlaceholderText = "Pregunta sobre la pista actual, formatos, DSD/FLAC, DACs, historia musical o cómo escuchar mejor",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 54,
            MaxHeight = 110,
            MaxWidth = dialogWidth
        };

        var statusTextBlock = new TextBlock
        {
            Text = _zenithAiClient.IsConfigured
                ? $"Conectado a {_zenithAiClient.Settings.Provider}. ZenitAI (BETA) solo responde temas de audio."
                : "Falta API key. Abre Ajustes de API para configurar NVIDIA NIM u otra API compatible.",
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = dialogWidth
        };

        var providerTextBox = new TextBox
        {
            Header = "Proveedor",
            Text = _zenithAiClient.Settings.Provider,
            PlaceholderText = "NVIDIA NIM, OpenAI compatible, servidor propio"
        };
        var endpointTextBox = new TextBox
        {
            Header = "Endpoint chat completions",
            Text = _zenithAiClient.Settings.Endpoint,
            PlaceholderText = "https://integrate.api.nvidia.com/v1/chat/completions",
            TextWrapping = TextWrapping.NoWrap
        };
        var modelTextBox = new TextBox
        {
            Header = "Modelo",
            Text = _zenithAiClient.Settings.Model,
            PlaceholderText = "google/gemma-4-31b-it"
        };
        var apiKeyBox = new PasswordBox
        {
            Header = "API key",
            Password = _zenithAiClient.Settings.ApiKey,
            PlaceholderText = "Pega aquí tu key"
        };
        var saveApiButton = new Button
        {
            Content = "Guardar API"
        };
        var resetApiButton = new Button
        {
            Content = "Usar NVIDIA NIM"
        };
        var apiButtonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        apiButtonsPanel.Children.Add(saveApiButton);
        apiButtonsPanel.Children.Add(resetApiButton);
        var apiSettingsPanel = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 10)
        };
        apiSettingsPanel.Children.Add(new TextBlock
        {
            Text = "Configura NVIDIA NIM u otra API compatible con OpenAI Chat Completions. Se guarda localmente por usuario.",
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = dialogWidth
        });
        apiSettingsPanel.Children.Add(providerTextBox);
        apiSettingsPanel.Children.Add(endpointTextBox);
        apiSettingsPanel.Children.Add(modelTextBox);
        apiSettingsPanel.Children.Add(apiKeyBox);
        apiSettingsPanel.Children.Add(apiButtonsPanel);

        var sendButton = new Button
        {
            Content = "Enviar",
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var clearButton = new Button
        {
            Content = "Limpiar",
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var configButton = new Button
        {
            Content = "Config",
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        buttonsPanel.Children.Add(sendButton);
        buttonsPanel.Children.Add(clearButton);
        buttonsPanel.Children.Add(configButton);

        var contentPanel = new Grid
        {
            Width = dialogWidth
        };
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var introTextBlock = new TextBlock
        {
            Text = "Asistente BETA especializado en historia musical, formatos, equipos y escucha crítica.",
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            MaxWidth = dialogWidth
        };
        contentPanel.Children.Add(introTextBlock);

        Grid.SetRow(transcriptScrollViewer, 1);
        transcriptScrollViewer.Margin = new Thickness(0, 0, 0, 12);
        contentPanel.Children.Add(transcriptScrollViewer);

        Grid.SetRow(questionTextBox, 2);
        questionTextBox.Margin = new Thickness(0, 0, 0, 10);
        contentPanel.Children.Add(questionTextBox);

        Grid.SetRow(buttonsPanel, 3);
        buttonsPanel.Margin = new Thickness(0, 0, 0, 10);
        contentPanel.Children.Add(buttonsPanel);

        Grid.SetRow(apiSettingsPanel, 4);
        contentPanel.Children.Add(apiSettingsPanel);

        Grid.SetRow(statusTextBlock, 5);
        contentPanel.Children.Add(statusTextBlock);

        var glassPanel = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Background = glassBrush,
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 95, 191, 255)),
            BorderThickness = new Thickness(1),
            Child = contentPanel
        };

        using var cancellationTokenSource = new CancellationTokenSource();
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "ZenitAI (BETA)",
            Content = glassPanel,
            CloseButtonText = "Cerrar",
            DefaultButton = ContentDialogButton.None
        };

        clearButton.Click += (_, _) =>
        {
            _zenithAiMessages.Clear();
            transcriptTextBlock.Text = FormatZenithAiTranscript();
            ScrollZenithAiTranscriptToEnd(transcriptScrollViewer);
            statusTextBlock.Text = "Historial limpio. Pregunta algo de audio.";
        };

        configButton.Click += (_, _) =>
        {
            apiSettingsPanel.Visibility = apiSettingsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            configButton.Content = apiSettingsPanel.Visibility == Visibility.Visible ? "Ocultar config" : "Config";
            statusTextBlock.Text = _zenithAiClient.IsConfigured
                ? $"Conectado a {_zenithAiClient.Settings.Provider}. ZenitAI (BETA) solo responde temas de audio."
                : "Falta API key. Usa Config para guardar NVIDIA NIM u otra API compatible.";
        };

        saveApiButton.Click += (_, _) =>
        {
            var settings = new ZenithAiSettings(
                string.IsNullOrWhiteSpace(providerTextBox.Text) ? "API compatible" : providerTextBox.Text.Trim(),
                endpointTextBox.Text.Trim(),
                modelTextBox.Text.Trim(),
                apiKeyBox.Password.Trim());
            ZenithAiSettings.Save(settings);
            _zenithAiClient.ReloadSettings();
            statusTextBlock.Text = _zenithAiClient.IsConfigured
                ? $"API guardada: {_zenithAiClient.Settings.Provider}."
                : "API guardada, pero falta API key.";
        };

        resetApiButton.Click += (_, _) =>
        {
            providerTextBox.Text = ZenithAiSettings.DefaultProvider;
            endpointTextBox.Text = ZenithAiSettings.DefaultEndpoint;
            modelTextBox.Text = ZenithAiSettings.DefaultModel;
            var settings = new ZenithAiSettings(
                ZenithAiSettings.DefaultProvider,
                ZenithAiSettings.DefaultEndpoint,
                ZenithAiSettings.DefaultModel,
                apiKeyBox.Password.Trim());
            ZenithAiSettings.Save(settings);
            _zenithAiClient.ReloadSettings();
            statusTextBlock.Text = "NVIDIA NIM restaurado. Pega o conserva tu API key y presiona Guardar API.";
        };

        sendButton.Click += async (_, _) =>
        {
            var question = questionTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(question))
            {
                return;
            }

            questionTextBox.Text = string.Empty;
            _zenithAiMessages.Add(new ZenithAiChatMessage("user", question));
            transcriptTextBlock.Text = FormatZenithAiTranscript("ZenitAI está pensando...");
            ScrollZenithAiTranscriptToEnd(transcriptScrollViewer);
            sendButton.IsEnabled = false;
            clearButton.IsEnabled = false;
            statusTextBlock.Text = "Consultando NVIDIA NIM en la nube...";

            try
            {
                var response = await _zenithAiClient.SendAsync(
                    _zenithAiMessages,
                    BuildZenithAiAudioContext(),
                    cancellationTokenSource.Token);

                _zenithAiMessages.Add(new ZenithAiChatMessage("assistant", response));
                transcriptTextBlock.Text = FormatZenithAiTranscript();
                ScrollZenithAiTranscriptToEnd(transcriptScrollViewer);
                statusTextBlock.Text = "Listo. ZenitAI no usa modelos locales ni carga el CPU/GPU para inferencia.";
            }
            catch (Exception ex)
            {
                _zenithAiMessages.Add(new ZenithAiChatMessage("assistant", $"No pude conectar con NVIDIA NIM: {ex.Message}"));
                transcriptTextBlock.Text = FormatZenithAiTranscript();
                ScrollZenithAiTranscriptToEnd(transcriptScrollViewer);
                statusTextBlock.Text = "Revisa conexion, API key o disponibilidad del modelo NVIDIA NIM.";
            }
            finally
            {
                sendButton.IsEnabled = true;
                clearButton.IsEnabled = true;
                ScrollZenithAiTranscriptToEnd(transcriptScrollViewer);
            }
        };

        await dialog.ShowAsync();
        cancellationTokenSource.Cancel();
    }

    private async void SelectSacdExtractorButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads
        };
        picker.FileTypeFilter.Add(".exe");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            var installedPath = SacdIsoExtractor.InstallTool(file.Path);
            UpdateSacdExtractorStatus();
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Message = $"Extractor SACD configurado: {installedPath}. Ahora puedes abrir SACD ISO y extraer DSF sin perdida.";
        }
        catch (Exception ex)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = $"No se pudo configurar sacd_extract.exe: {ex.Message}";
        }
    }

    private void UpdateSacdExtractorStatus()
    {
        if (SacdToolStatusTextBlock is null)
        {
            return;
        }

        var toolPath = SacdIsoExtractor.CurrentToolPath;
        var isConfigured = toolPath is not null;
        SacdToolStatusTextBlock.Text = isConfigured
            ? $"Extractor SACD incluido y listo: {toolPath}"
            : "sacd_extract.exe no configurado. Necesario para leer SACD ISO y extraer DSF sin perdida.";

        if (SacdToolSelectButton is not null)
        {
            SacdToolSelectButton.Visibility = isConfigured ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void DsdModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePlaybackAvailabilityV2();
    }

    private void ExclusiveModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (ModeTextBlock is null || OutputPathTextBlock is null)
        {
            return;
        }

        ApplyOutputDeviceSettings();
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
    }

    private void LatencySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (BufferSlider is null || BufferTextBlock is null)
        {
            return;
        }

        BufferSlider.Value = e.NewValue;
        BufferTextBlock.Text = $"{(int)e.NewValue} ms";
        _bufferProfile = "latencia objetivo";
        UpdateSignalChain();
    }

    private void DitherPolicyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSignalChain();
    }

    private void ToneControl_Changed(object sender, RoutedEventArgs e)
    {
        UpdateToneControlState(showStatus: false);
    }

    private void ToneSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (PreampSlider is null || SubBassSlider is null || PresenceSlider is null || AirSlider is null)
        {
            return;
        }

        UpdateToneControlState(showStatus: true);
    }

    private void EqPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreampSlider is null)
        {
            return;
        }

        switch (EqPresetComboBox.SelectedIndex)
        {
            case 1:
                EqToggleButton.IsChecked = true;
                DspBypassToggleButton.IsChecked = false;
                PreampSlider.Value = -2;
                SubBassSlider.Value = 2.5;
                PresenceSlider.Value = 1.5;
                AirSlider.Value = 1;
                break;
            case 2:
                EqToggleButton.IsChecked = true;
                DspBypassToggleButton.IsChecked = false;
                PreampSlider.Value = -3;
                SubBassSlider.Value = 1.5;
                PresenceSlider.Value = 2;
                AirSlider.Value = 2.5;
                break;
            case 3:
                EqToggleButton.IsChecked = true;
                DspBypassToggleButton.IsChecked = false;
                PreampSlider.Value = -4;
                SubBassSlider.Value = 3;
                PresenceSlider.Value = -1;
                AirSlider.Value = 2;
                break;
            default:
                ResetToneControls(showStatus: false);
                break;
        }

        UpdateToneControlState(showStatus: true);
    }

    private void AutoEqSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        EqPresetComboBox.SelectedIndex = 3;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = $"Perfil AutoEQ seleccionado: {args.QueryText}. Perfil preparado como curva base local.";
    }

    private void ResetToneButton_Click(object sender, RoutedEventArgs e)
    {
        ResetToneControls(showStatus: true);
    }

    private void ResetToneControls(bool showStatus)
    {
        EqToggleButton.IsChecked = false;
        DspBypassToggleButton.IsChecked = true;
        PreampSlider.Value = 0;
        SubBassSlider.Value = 0;
        PresenceSlider.Value = 0;
        AirSlider.Value = 0;
        EqPresetComboBox.SelectedIndex = 0;
        UpdateToneControlState(showStatus);
    }

    private void UpdateToneControlState(bool showStatus)
    {
        if (ToneSummaryTextBlock is null)
        {
            return;
        }

        _toneSettings.EqEnabled = EqToggleButton.IsChecked == true;
        _toneSettings.DspBypassed = DspBypassToggleButton.IsChecked == true;
        _toneSettings.PreampDb = PreampSlider.Value;
        _toneSettings.SubBassDb = SubBassSlider.Value;
        _toneSettings.PresenceDb = PresenceSlider.Value;
        _toneSettings.AirDb = AirSlider.Value;

        ToneSummaryTextBlock.Text = _toneSettings.IsActive
            ? $"Pre {FormatDb(_toneSettings.PreampDb)} | Sub {FormatDb(_toneSettings.SubBassDb)} | Presencia {FormatDb(_toneSettings.PresenceDb)} | Aire {FormatDb(_toneSettings.AirDb)}"
            : "DSP omitido | Plano";
        UpdateSignalChain();

        if (!showStatus)
        {
            return;
        }

        StatusInfoBar.Severity = _toneSettings.IsActive ? InfoBarSeverity.Informational : InfoBarSeverity.Success;
        StatusInfoBar.Message = _toneSettings.IsActive
            ? "Control de tono activo en RAM y en tiempo real para DSF RAM stream. FLAC/MP3 por Windows requieren DSP Media Foundation o EQ de MPV/BASS."
            : "Control de tono en bypass: salida plana.";
    }

    private void PlaybackProgressSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingPlaybackProgress)
        {
            return;
        }

        SeekFallbackPlayer(TimeSpan.FromSeconds(e.NewValue));
    }

    private void PlaybackProgressSlider_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isSeeking = true;
    }

    private void PlaybackProgressSlider_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SeekFallbackPlayer(TimeSpan.FromSeconds(PlaybackProgressSlider.Value));
        _isSeeking = false;
    }

    private void ScanThreadsSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (StatusInfoBar is null)
        {
            return;
        }

        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = $"Escáner de biblioteca configurado en {(int)e.NewValue} hilos";
    }

    private void UseWindowsMusicButton_Click(object sender, RoutedEventArgs e)
    {
        var musicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrWhiteSpace(musicPath))
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = "No se encontro la carpeta Musica de Windows";
            return;
        }

        AddLibraryFolder(musicPath);
    }

    private async void AddMusicFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary
        };

        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        AddLibraryFolder(folder.Path);
    }

    private void ShowMusicFoldersButton_Click(object sender, RoutedEventArgs e)
    {
        ShowFolderBrowser();
    }

    private void ShowDsdLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        ShowTrackBrowser("Álbumes DSD", _libraryTracks.Where(track => DsdExtensions.Contains(track.Extension)));
    }

    private void ShowHiResLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        ShowTrackBrowser("Hi-Res PCM", _libraryTracks.Where(track => HiResPcmExtensions.Contains(track.Extension)));
    }

    private void ShowSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        RightPanelScrollViewer.ChangeView(null, RightPanelScrollViewer.ScrollableHeight, null);
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = "Los ajustes avanzados están disponibles en el panel derecho";
    }

    private void LibrarySearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        ApplyLibrarySearch();
    }

    private void LibraryBrowserListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LibraryTrack track)
        {
            if (track.Extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                _ = OpenIsoImageAsync(track.Path);
                return;
            }

            if (track.Extension.Equals(".cue", StringComparison.OrdinalIgnoreCase))
            {
                _ = OpenCueSheetAsync(track.Path);
                return;
            }

            LoadTrack(track.Path);
            return;
        }

        if (e.ClickedItem is string folder)
        {
            LibraryFoldersListView.SelectedItem = folder;
            ShowTrackBrowser(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } folderName ? folderName : folder,
                _libraryTracks.Where(track => track.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private async void LibraryBrowserListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (LibraryBrowserListView.SelectedItem is not LibraryTrack track)
        {
            return;
        }

        StopCurrentPlaybackForTrackChange();
        if (track.Extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
        {
            await OpenIsoImageAsync(track.Path);
            return;
        }

        if (track.Extension.Equals(".cue", StringComparison.OrdinalIgnoreCase))
        {
            await OpenCueSheetAsync(track.Path);
            return;
        }

        LoadTrack(track.Path);
        PlayButton_Click(this, null!);
    }

    private void AudioEngine_PlaybackStateChanged(object? sender, PlaybackState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusInfoBar.Message = FormatPlaybackState(state);
            StatusInfoBar.Severity = state == PlaybackState.Error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
            TransportBadgeTextBlock.Text = FormatPlaybackState(state).ToUpperInvariant();
        });
    }

    private void AudioEngine_SignalChanged(object? sender, AudioSignalInfo signal)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            BitrateTextBlock.Text = signal.BitrateKbps > 0 ? $"{signal.BitrateKbps:N0} kbps" : "-- kbps";
            SampleRateTextBlock.Text = signal.SampleRate > 0 ? $"{signal.SampleRate / 1000.0:N1} kHz" : "-- kHz";
            BitDepthTextBlock.Text = signal.IsDsd ? "1-bit DSD" : $"{signal.BitDepth} bit";
            ChannelsTextBlock.Text = signal.Channels > 0 ? signal.Channels.ToString() : "--";
            CodecTextBlock.Text = string.IsNullOrWhiteSpace(signal.Codec) ? "Desconocido" : signal.Codec;
            UpdateSignalChain("Decodificación nativa");
        });
    }

    private void FallbackMediaPlayer_MediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var duration = sender.PlaybackSession.NaturalDuration;
            PlaybackProgressSlider.Maximum = Math.Max(duration.TotalSeconds, 1);
            DurationTextBlock.Text = FormatTime(duration);
            if (_pendingCueEntry is not null && _pendingCueEntry.Start > TimeSpan.Zero)
            {
                sender.PlaybackSession.Position = _pendingCueEntry.Start;
                CurrentTimeTextBlock.Text = FormatTime(_pendingCueEntry.Start);
            }
            else
            {
                CurrentTimeTextBlock.Text = "0:00";
            }
            _currentLyricIndex = -1;
            UpdateSyncedLyrics(_pendingCueEntry?.Start ?? TimeSpan.Zero);
            _playbackTimer.Start();
        });
    }

    private void FallbackMediaPlayer_MediaEnded(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _playbackTimer.Stop();
            _vuTimer.Stop();
            _fallbackMediaPlayer.Source = null;
            CleanupTemporaryFallbackFile();
            _vuUsesLiveLevel = false;
            ResetVuMeter();
            TransportBadgeTextBlock.Text = "FINALIZADO";
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            PlaybackProgressSlider.Value = 0;
            CurrentTimeTextBlock.Text = "0:00";
            _ = PlayNextTrackAsync(manual: false);
        });
    }

    private void PlaybackTimer_Tick(object? sender, object e)
    {
        if (!_usingFallbackPlayer || _isSeeking)
        {
            return;
        }

        var session = _fallbackMediaPlayer.PlaybackSession;
        var position = session.Position;
        var duration = session.NaturalDuration;

        _isUpdatingPlaybackProgress = true;
        PlaybackProgressSlider.Maximum = Math.Max(duration.TotalSeconds, 1);
        PlaybackProgressSlider.Value = Math.Clamp(position.TotalSeconds, 0, PlaybackProgressSlider.Maximum);
        _isUpdatingPlaybackProgress = false;
        CurrentTimeTextBlock.Text = FormatTime(position);
        DurationTextBlock.Text = FormatTime(duration);
        UpdateSyncedLyrics(position);

        if (_pendingCueEntry?.End is { } cueEnd && position >= cueEnd)
        {
            StopButton_Click(this, new RoutedEventArgs());
            _ = PlayNextTrackAsync(manual: false);
        }
    }

    private void SeekFallbackPlayer(TimeSpan position)
    {
        if (!_usingFallbackPlayer)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = "Seek desde la barra está disponible en reproducción Windows/PCM fallback.";
            return;
        }

        var session = _fallbackMediaPlayer.PlaybackSession;
        var duration = session.NaturalDuration;
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var clampedSeconds = Math.Clamp(position.TotalSeconds, 0, duration.TotalSeconds);
        var clampedPosition = TimeSpan.FromSeconds(clampedSeconds);
        session.Position = clampedPosition;
        CurrentTimeTextBlock.Text = FormatTime(clampedPosition);
    }

    private void UpdateSignalChain(string? decodeOverride = null)
    {
        if (SignalSourceTextBlock is null ||
            SignalDecodeTextBlock is null ||
            SignalDspTextBlock is null ||
            SignalOutputTextBlock is null ||
            SignalBitPerfectTextBlock is null ||
            SignalDitherTextBlock is null ||
            SignalBufferTextBlock is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            SignalSourceTextBlock.Text = "Sin archivo";
            SignalDecodeTextBlock.Text = "Inactivo";
            SignalDspTextBlock.Text = _toneSettings.IsActive ? "Tono activo" : "Bypass";
            SignalOutputTextBlock.Text = GetSignalOutputLabel();
            SignalBitPerfectTextBlock.Text = "Sin archivo cargado";
            SignalDitherTextBlock.Text = GetDitherPolicyLabel();
            SignalBufferTextBlock.Text = $"{(int)(BufferSlider?.Value ?? 100)} ms | {_bufferProfile}";
            UpdateSignalVisualState(hasTrack: false);
            return;
        }

        var effectivePath = GetEffectivePlaybackPath(_currentFilePath);
        var currentExtension = Path.GetExtension(effectivePath);
        var extension = currentExtension.TrimStart('.').ToUpperInvariant();
        var sourceRate = DsdExtensions.Contains(currentExtension)
            ? "Fuente DSD"
            : SampleRateTextBlock.Text;
        SignalSourceTextBlock.Text = $"{extension} | {sourceRate}";

        var backend = GetSelectedBackend();
        var decode = decodeOverride;
        if (string.IsNullOrWhiteSpace(decode))
        {
            if (_usingFallbackPlayer && DsdExtensions.Contains(currentExtension))
            {
                decode = "Decodificación DSD a PCM en stream";
            }
            else if (_usingFallbackPlayer)
            {
                decode = "Windows Media Foundation";
            }
            else if (IsNativeBackendAvailable(backend, effectivePath))
            {
                decode = backend == AudioBackend.MpvWasapi ? "MPV WASAPI" : "BASS WASAPI";
            }
            else if (DsdExtensions.Contains(currentExtension))
            {
                decode = currentExtension.Equals(".dsf", StringComparison.OrdinalIgnoreCase)
                    ? "DSF -> PCM en RAM"
                    : "DSD nativo requerido";
            }
            else
            {
                decode = "Fallback de Windows";
            }
        }

        SignalDecodeTextBlock.Text = decode;
        SignalDspTextBlock.Text = _toneSettings.IsActive
            ? $"EQ de tono | {ToneSummaryTextBlock.Text}"
            : "Bypass | bit-transparente";
        SignalOutputTextBlock.Text = GetSignalOutputLabel();
        SignalBitPerfectTextBlock.Text = GetBitPerfectStatus(_currentFilePath, decode);
        SignalDitherTextBlock.Text = GetDitherStatus(_currentFilePath);
        SignalBufferTextBlock.Text = $"{(int)(BufferSlider?.Value ?? 100)} ms | {_bufferProfile}";
        UpdateSignalVisualState(hasTrack: true);
    }

    private void UpdateSignalVisualState(bool hasTrack)
    {
        var status = SignalBitPerfectTextBlock?.Text ?? string.Empty;
        var strict = status.Contains("Probable bit-perfect", StringComparison.OrdinalIgnoreCase);
        var dspActive = _toneSettings.IsActive;
        var warning = status.Contains("No confirmado", StringComparison.OrdinalIgnoreCase) ||
                      status.Contains("Windows", StringComparison.OrdinalIgnoreCase);
        var error = status.Contains("No bit-perfect", StringComparison.OrdinalIgnoreCase) ||
                    status.Contains("convertido", StringComparison.OrdinalIgnoreCase);

        if (BitPerfectStatusLed is not null)
        {
            BitPerfectStatusLed.Fill = new SolidColorBrush(
                strict ? Microsoft.UI.ColorHelper.FromArgb(255, 0, 210, 168) :
                dspActive || warning ? Microsoft.UI.ColorHelper.FromArgb(255, 245, 184, 65) :
                error ? Microsoft.UI.ColorHelper.FromArgb(255, 235, 84, 84) :
                Microsoft.UI.ColorHelper.FromArgb(255, 154, 160, 166));
        }

        SetSignalFlowPill(SourceFlowPill, hasTrack, hasTrack && DsdExtensions.Contains(Path.GetExtension(_currentFilePath ?? string.Empty)));
        SetSignalFlowPill(DecoderFlowPill, hasTrack, _usingFallbackPlayer);
        SetSignalFlowPill(DspFlowPill, hasTrack && dspActive, hasTrack && dspActive);
        SetSignalFlowPill(OutputFlowPill, hasTrack, hasTrack && (!ExclusiveModeToggle.IsOn || _usingFallbackPlayer));
    }

    private static void SetSignalFlowPill(Border? pill, bool active, bool attention)
    {
        if (pill is null)
        {
            return;
        }

        pill.Background = new SolidColorBrush(active
            ? attention
                ? Microsoft.UI.ColorHelper.FromArgb(255, 68, 50, 15)
                : Microsoft.UI.ColorHelper.FromArgb(255, 12, 60, 78)
            : Microsoft.UI.ColorHelper.FromArgb(255, 24, 34, 53));
        pill.BorderBrush = new SolidColorBrush(active
            ? attention
                ? Microsoft.UI.ColorHelper.FromArgb(255, 221, 166, 55)
                : Microsoft.UI.ColorHelper.FromArgb(255, 94, 191, 255)
            : Microsoft.UI.ColorHelper.FromArgb(255, 58, 70, 92));
        pill.Opacity = active ? 1.0 : 0.55;
    }

    private void AnalyzeCurrentTrackButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            LabAuthenticityTextBlock.Text = "Autenticidad: sin archivo cargado";
            LabScoreTextBlock.Text = "-- / 100";
            LabVerdictTextBlock.Text = "Sin análisis";
            LabFormatTextBlock.Text = "--";
            LabContainerTextBlock.Text = "Esperando archivo";
            LabResolutionTextBlock.Text = "--";
            LabRiskTextBlock.Text = "--";
            LabFindingTextBlock.Text = "Carga una pista antes de ejecutar el laboratorio Anti-Fake.";
            LabRecommendationTextBlock.Text = string.Empty;
            return;
        }

        var report = BuildAntiFakeReport(_currentFilePath);

        LabAuthenticityTextBlock.Text = report.Authenticity;
        LabScoreTextBlock.Text = $"{report.Score} / 100";
        LabVerdictTextBlock.Text = report.Verdict;
        LabFormatTextBlock.Text = report.Format;
        LabContainerTextBlock.Text = report.Container;
        LabResolutionTextBlock.Text = report.Resolution;
        LabRiskTextBlock.Text = report.Risk;
        LabFindingTextBlock.Text = report.Finding;
        LabRecommendationTextBlock.Text = report.Recommendation;

        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = $"Laboratorio Anti-Fake: reporte generado para {report.Format}. Indice preliminar {report.Score}/100.";
        if (report.Score >= 0)
        {
            return;
        }

        var extension = Path.GetExtension(_currentFilePath).ToUpperInvariant();
        var sampleRate = SampleRateTextBlock.Text;
        var bitDepth = BitDepthTextBlock.Text;
        var codec = CodecTextBlock.Text;

        if (DsdExtensions.Contains(Path.GetExtension(_currentFilePath)))
        {
            LabAuthenticityTextBlock.Text = "Autenticidad: alta | Fuente DSD detectada";
            LabFindingTextBlock.Text = $"Archivo {extension} con ruta DSD. Zenith no detecta corte PCM por metadatos; el siguiente paso técnico es medir espectro real y ruido ultrasónico con FFT.";
        }
        else if (HiResPcmExtensions.Contains(Path.GetExtension(_currentFilePath)))
        {
            LabAuthenticityTextBlock.Text = "Autenticidad: preliminar | PCM Hi-Res";
            LabFindingTextBlock.Text = $"Codec {codec}, frecuencia {sampleRate}, profundidad {bitDepth}. Recomendado: análisis espectral para confirmar ausencia de brickwall en 22 kHz y clipping intersample.";
        }
        else
        {
            LabAuthenticityTextBlock.Text = "Autenticidad: no Hi-Res";
            LabFindingTextBlock.Text = $"Formato {extension}. Puede reproducirse correctamente, pero no corresponde a una fuente audiófila sin pérdida para auditoría Hi-Res.";
        }

        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = "Laboratorio Anti-Fake: análisis preliminar completado con metadatos. Espectrograma FFT real queda preparado como siguiente módulo.";
    }

    private AntiFakeReport BuildAntiFakeReport(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var extensionLabel = extension.TrimStart('.').ToUpperInvariant();
        var codec = CleanMetric(CodecTextBlock.Text, extensionLabel);
        var sampleRate = CleanMetric(SampleRateTextBlock.Text, "-- kHz");
        var bitDepth = CleanMetric(BitDepthTextBlock.Text, "-- bit");
        var bitrate = CleanMetric(BitrateTextBlock.Text, "-- kbps");
        var sizeLabel = GetFileSizeLabel(filePath);
        var outputStatus = SignalBitPerfectTextBlock?.Text ?? "Sin validar";
        var dspStatus = _toneSettings.IsActive ? "DSP/EQ activo" : "DSP en bypass";

        if (extension is ".mp3" or ".aac" or ".ogg" or ".m4a")
        {
            var score = extension == ".mp3" ? 35 : 42;
            return new AntiFakeReport(
                score,
                "Autenticidad: formato básico / con pérdida",
                "Correcto para escucha casual; no es fuente audiófila de archivo maestro.",
                extensionLabel,
                $"{codec} | {sizeLabel}",
                $"{sampleRate} | {bitrate}",
                "Alto para auditoría Hi-Res",
                $"El archivo {extensionLabel} usa compresión con pérdida o depende de codecs del sistema. No se considera falso: simplemente no contiene toda la información del máster original.",
                "Usar como biblioteca diaria está bien. Para evaluación de DAC, dinámica o remasterizaciones, preferir FLAC/WAV/AIFF o DSD.");
        }

        if (extension is ".dsf" or ".dff")
        {
            var dsdLabel = DetectDsdRateLabel(filePath);
            var score = _usingFallbackPlayer ? 88 : 96;
            var risk = _usingFallbackPlayer ? "Conversión DSD -> PCM activa" : "Bajo si la cadena es nativa";
            return new AntiFakeReport(
                score,
                "Autenticidad: alta | Fuente DSD detectada",
                _usingFallbackPlayer ? "DSD válido, reproducido mediante conversión temporal a PCM." : "Ruta DSD compatible con reproducción nativa o backend dedicado.",
                extensionLabel,
                $"{dsdLabel} | {sizeLabel}",
                $"{sampleRate} | 1-bit sigma-delta",
                risk,
                $"Archivo {extensionLabel} identificado como DSD. En un análisis FFT real debería verse ruido ultrasónico progresivo, no un corte limpio tipo PCM/CD en 22 kHz.",
                $"Cadena actual: {outputStatus}. {dspStatus}. Para máxima pureza, usar modo exclusivo, DSP omitido y backend nativo cuando esté disponible.");
        }

        if (extension == ".iso")
        {
            return new AntiFakeReport(
                92,
                "Autenticidad: alta | Contenedor SACD ISO",
                "La calidad depende de las pistas DSF extraídas desde el ISO.",
                "SACD ISO",
                $"{codec} | {sizeLabel}",
                "DSD64 típico de SACD | 1-bit",
                "Bajo si la extracción DSF es directa",
                "El ISO SACD no debe convertirse con pérdida. Zenith usa extracción temporal a DSF cuando sacd_extract está disponible.",
                "Reproducir una pista extraída y ejecutar nuevamente el reporte por pista para validar ruta DSD, buffer y salida.");
        }

        if (extension is ".flac" or ".wav" or ".aiff" or ".aif" or ".alac" or ".mqa")
        {
            var sampleRateValue = ParseKHz(sampleRate);
            var score = sampleRateValue >= 88.2 ? 82 : 72;
            var tier = sampleRateValue >= 88.2 ? "PCM Hi-Res sin pérdida" : "PCM sin pérdida estándar";
            var risk = sampleRateValue >= 88.2 ? "Medio: requiere FFT para descartar upsample" : "Bajo: resolución coherente con CD/PCM";
            if (extension == ".mqa" || codec.Contains("MQA", StringComparison.OrdinalIgnoreCase))
            {
                score = 68;
                tier = "MQA / PCM encapsulado";
                risk = "Medio-alto: depende de decodificación MQA";
            }

            return new AntiFakeReport(
                score,
                $"Autenticidad: preliminar | {tier}",
                sampleRateValue >= 88.2 ? "El contenedor declara alta resolución; falta confirmar espectro real." : "Archivo sin pérdida, adecuado para escucha crítica estándar.",
                extensionLabel,
                $"{codec} | {sizeLabel}",
                $"{sampleRate} | {bitDepth} | {bitrate}",
                risk,
                $"Zenith detecta {codec} con {sampleRate}, {bitDepth} y {bitrate}. El punto crítico es verificar si existe energía musical por encima de 22 kHz o si hay brickwall de fuente CD.",
                $"Cadena actual: {outputStatus}. {dspStatus}. Para análisis serio, repetir con DSP omitido y salida exclusiva.");
        }

        return new AntiFakeReport(
            50,
            "Autenticidad: desconocida",
            "Formato reproducible, pero sin perfil audiófilo específico.",
            extensionLabel,
            $"{codec} | {sizeLabel}",
            $"{sampleRate} | {bitDepth} | {bitrate}",
            "No clasificado",
            "Zenith no tiene reglas suficientes para clasificar este formato con confianza.",
            "Usar FLAC/WAV/AIFF/DSF para pruebas audiófilas comparables.");
    }

    private static string CleanMetric(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static double ParseKHz(string value)
    {
        var numeric = value
            .Replace("kHz", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .Replace(',', '.');
        return double.TryParse(numeric, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static string GetFileSizeLabel(string filePath)
    {
        try
        {
            var bytes = new FileInfo(filePath).Length;
            return bytes >= 1024L * 1024L * 1024L
                ? $"{bytes / 1024d / 1024d / 1024d:N2} GB"
                : $"{bytes / 1024d / 1024d:N1} MB";
        }
        catch
        {
            return "tamaño no disponible";
        }
    }

    private static string DetectDsdRateLabel(string filePath)
    {
        var text = filePath.ToUpperInvariant();
        if (text.Contains("DSD1024", StringComparison.OrdinalIgnoreCase))
        {
            return "DSD1024";
        }

        if (text.Contains("DSD512", StringComparison.OrdinalIgnoreCase))
        {
            return "DSD512";
        }

        if (text.Contains("DSD256", StringComparison.OrdinalIgnoreCase))
        {
            return "DSD256";
        }

        if (text.Contains("DSD128", StringComparison.OrdinalIgnoreCase))
        {
            return "DSD128";
        }

        if (text.Contains("DSD64", StringComparison.OrdinalIgnoreCase) || text.Contains("SACD", StringComparison.OrdinalIgnoreCase))
        {
            return "DSD64 / SACD";
        }

        return "DSD";
    }

    private sealed record AntiFakeReport(
        int Score,
        string Authenticity,
        string Verdict,
        string Format,
        string Container,
        string Resolution,
        string Risk,
        string Finding,
        string Recommendation);

    private void ApplyAdaptiveBufferForTrack(string filePath)
    {
        if (BufferSlider is null || BufferTextBlock is null)
        {
            return;
        }

        var recommended = GetRecommendedBufferMilliseconds(filePath);
        if ((int)BufferSlider.Value >= recommended)
        {
            _bufferProfile = GetBufferProfileLabel(filePath, (int)BufferSlider.Value);
            return;
        }

        _isApplyingAdaptiveBuffer = true;
        BufferSlider.Value = recommended;
        _isApplyingAdaptiveBuffer = false;
        BufferTextBlock.Text = $"{recommended} ms";
        _bufferProfile = GetBufferProfileLabel(filePath, recommended);
    }

    private static int GetRecommendedBufferMilliseconds(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
        {
            return 350;
        }

        if (extension.Equals(".dsf", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".dff", StringComparison.OrdinalIgnoreCase))
        {
            var sizeGb = new FileInfo(filePath).Length / 1024d / 1024d / 1024d;
            return sizeGb switch
            {
                >= 3.0 => 500,
                >= 1.5 => 350,
                _ => 250
            };
        }

        return 100;
    }

    private static string GetBufferProfileLabel(string filePath, int bufferMilliseconds)
    {
        var extension = Path.GetExtension(filePath);
        if (extension.Equals(".dsf", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".dff", StringComparison.OrdinalIgnoreCase))
        {
            return bufferMilliseconds >= 350 ? "DSD extremo" : "DSD estable";
        }

        if (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
        {
            return "SACD/ISO";
        }

        return bufferMilliseconds <= 120 ? "PCM baja latencia" : "PCM estable";
    }

    private string GetBitPerfectStatus(string filePath, string? decode)
    {
        if (_toneSettings.IsActive)
        {
            return "No bit-perfect: DSP/EQ activo";
        }

        if (_usingFallbackPlayer)
        {
            return DsdExtensions.Contains(Path.GetExtension(filePath))
                ? "No bit-perfect: DSD convertido a PCM"
                : "No confirmado: Windows Media Foundation";
        }

        if (!ExclusiveModeToggle.IsOn)
        {
            return "No confirmado: modo compartido";
        }

        if (decode?.Contains("nativa", StringComparison.OrdinalIgnoreCase) == true ||
            decode?.Contains("BASS WASAPI", StringComparison.OrdinalIgnoreCase) == true ||
            decode?.Contains("MPV WASAPI", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Probable bit-perfect: salida exclusiva sin DSP";
        }

        return "No confirmado";
    }

    private string GetDitherStatus(string filePath)
    {
        var policy = DitherPolicyComboBox?.SelectedIndex ?? 2;
        var label = GetDitherPolicyLabel();
        if (policy == 0)
        {
            return $"{label} | truncamiento manual";
        }

        if (DsdExtensions.Contains(Path.GetExtension(filePath)) && _usingFallbackPlayer)
        {
            return $"{label} | aplicable al bajar a PCM fijo";
        }

        return $"{label} | sin reducción detectada";
    }

    private string GetDitherPolicyLabel()
    {
        return (DitherPolicyComboBox?.SelectedIndex ?? 2) switch
        {
            0 => "Nunca",
            1 => "Siempre",
            2 => "Solo al reducir bits",
            3 => "Automático audiófilo",
            _ => "Solo al reducir bits"
        };
    }

    private void UpdateNowPlayingVisuals(string filePath)
    {
        AlbumTitleTextBlock.Text = Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "Carátula del álbum";
        LoadCoverArt(filePath);
        LoadLyrics(filePath);
        ResetVuMeter();
    }

    private async void LoadCoverArt(string filePath)
    {
        if (await TryLoadEmbeddedCoverArtAsync(filePath))
        {
            return;
        }

        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(folder))
        {
            ShowCoverPlaceholder("Sin carpeta de álbum");
            return;
        }

        var coverPath = new[]
            {
                "cover.jpg",
                "cover.png",
                "folder.jpg",
                "folder.png",
                "front.jpg",
                "front.png"
            }
            .Select(name => Path.Combine(folder, name))
            .FirstOrDefault(File.Exists);

        if (coverPath is null)
        {
            ShowCoverPlaceholder("Agrega cover.jpg/png junto a la música");
            return;
        }

        try
        {
            CoverArtImage.Source = new BitmapImage(new Uri(coverPath));
            CoverArtImage.Visibility = Visibility.Visible;
            CoverPlaceholderPanel.Visibility = Visibility.Collapsed;
            CoverHintTextBlock.Text = Path.GetFileName(coverPath);
        }
        catch (Exception)
        {
            ShowCoverPlaceholder("No se pudo cargar la caratula");
        }
    }

    private async Task<bool> TryLoadEmbeddedCoverArtAsync(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var picture = tagFile.Tag.Pictures.FirstOrDefault();
            if (picture is null || picture.Data.Count == 0)
            {
                return false;
            }

            var bytes = picture.Data.Data;
            using var randomAccessStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(randomAccessStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
            }

            randomAccessStream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(randomAccessStream);
            CoverArtImage.Source = image;
            CoverArtImage.Visibility = Visibility.Visible;
            CoverPlaceholderPanel.Visibility = Visibility.Collapsed;
            CoverHintTextBlock.Text = "Carátula embebida";
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ShowCoverPlaceholder(string hint)
    {
        CoverArtImage.Source = null;
        CoverArtImage.Visibility = Visibility.Collapsed;
        CoverPlaceholderPanel.Visibility = Visibility.Visible;
        CoverHintTextBlock.Text = hint;
    }

    private void LoadLyrics(string filePath)
    {
        _syncedLyrics = [];
        _currentLyricIndex = -1;
        if (TryLoadEmbeddedLyrics(filePath))
        {
            return;
        }

        var folder = Path.GetDirectoryName(filePath);
        var name = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name))
        {
            ShowNoLyrics();
            return;
        }

        var lyricPath = new[]
            {
                Path.Combine(folder, $"{name}.lrc"),
                Path.Combine(folder, $"{name}.txt"),
                Path.Combine(folder, "lyrics.lrc"),
                Path.Combine(folder, "lyrics.txt")
            }
            .FirstOrDefault(File.Exists);

        if (lyricPath is null)
        {
            ShowNoLyrics();
            return;
        }

        try
        {
            var lines = ReadLyricLines(lyricPath);
            _syncedLyrics = ParseSyncedLyrics(lines);
            var cleanLines = lines
                .Select(CleanLyricLine)
                .Select(RepairMojibake)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(120);

            LyricsTextBlock.Text = string.Join(Environment.NewLine, cleanLines);
            LyricsStatusTextBlock.Text = Path.GetExtension(lyricPath).TrimStart('.').ToUpperInvariant();
            CurrentLyricTextBlock.Text = _syncedLyrics.Count > 0 ? "Letra sincronizada lista" : "Letra sin sincronización";
            LyricsTextBlock.Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        }
        catch (Exception)
        {
            ShowNoLyrics();
            LyricsStatusTextBlock.Text = "error";
        }
    }

    private bool TryLoadEmbeddedLyrics(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var lyrics = tagFile.Tag.Lyrics;
            if (string.IsNullOrWhiteSpace(lyrics))
            {
                return false;
            }

            var lines = RepairMojibake(lyrics).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            _syncedLyrics = ParseSyncedLyrics(lines);
            LyricsTextBlock.Text = string.Join(
                Environment.NewLine,
                lines.Select(CleanLyricLine)
                    .Select(RepairMojibake)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Take(120));
            LyricsStatusTextBlock.Text = "embebida";
            CurrentLyricTextBlock.Text = _syncedLyrics.Count > 0 ? "Letra sincronizada lista" : "Letra embebida sin sincronización";
            LyricsTextBlock.Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ShowNoLyrics()
    {
        LyricsStatusTextBlock.Text = "local";
        CurrentLyricTextBlock.Text = "Letra sincronizada no disponible";
        LyricsTextBlock.Text = "Sin letra local. Zenith buscar\u00e1 un archivo .lrc o .txt con el mismo nombre de la canci\u00f3n.";
        LyricsTextBlock.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    }

    private static List<string> ReadLyricLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var text = DecodeText(bytes);
        return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(RepairMojibake)
            .ToList();
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }
        }

        try
        {
            var utf8 = new UTF8Encoding(false, true).GetString(bytes);
            return utf8.Contains('\uFFFD', StringComparison.Ordinal) ? Encoding.Latin1.GetString(bytes) : utf8;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static string RepairMojibake(string text)
    {
        if (string.IsNullOrEmpty(text) ||
            (!text.Contains('\u00c3', StringComparison.Ordinal) && !text.Contains('\u00c2', StringComparison.Ordinal)))
        {
            return text;
        }

        try
        {
            return Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(text));
        }
        catch (DecoderFallbackException)
        {
            return text;
        }
    }
    private static string CleanLyricLine(string line)
    {
        var cleaned = line.Trim();
        while (cleaned.StartsWith("[", StringComparison.Ordinal))
        {
            var close = cleaned.IndexOf(']', StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            cleaned = cleaned[(close + 1)..].TrimStart();
        }

        return cleaned;
    }

    private static List<SyncedLyricLine> ParseSyncedLyrics(IEnumerable<string> lines)
    {
        var synced = new List<SyncedLyricLine>();
        foreach (var line in lines)
        {
            var remaining = line.Trim();
            var timestamps = new List<TimeSpan>();

            while (remaining.StartsWith("[", StringComparison.Ordinal))
            {
                var close = remaining.IndexOf(']', StringComparison.Ordinal);
                if (close < 0)
                {
                    break;
                }

                var token = remaining[1..close];
                if (TryParseLrcTimestamp(token, out var timestamp))
                {
                    timestamps.Add(timestamp);
                }

                remaining = remaining[(close + 1)..].TrimStart();
            }

            if (timestamps.Count == 0 || string.IsNullOrWhiteSpace(remaining))
            {
                continue;
            }

            foreach (var timestamp in timestamps)
            {
                synced.Add(new SyncedLyricLine(timestamp, remaining));
            }
        }

        synced.Sort((left, right) => left.Time.CompareTo(right.Time));
        return synced;
    }

    private static bool TryParseLrcTimestamp(string value, out TimeSpan timestamp)
    {
        timestamp = TimeSpan.Zero;
        var parts = value.Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var minutes) ||
            !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        timestamp = TimeSpan.FromSeconds((minutes * 60) + seconds);
        return true;
    }

    private void UpdateSyncedLyrics(TimeSpan position)
    {
        if (_syncedLyrics.Count == 0)
        {
            return;
        }

        var index = _syncedLyrics.FindLastIndex(line => line.Time <= position + TimeSpan.FromMilliseconds(180));
        if (index < 0 || index == _currentLyricIndex)
        {
            return;
        }

        _currentLyricIndex = index;
        CurrentLyricTextBlock.Text = _syncedLyrics[index].Text;
    }

    private void StartVuMeter()
    {
        if (!_vuTimer.IsRunning)
        {
            _vuTimer.Start();
        }
    }

    private void VuTimer_Tick(object? sender, object e)
    {
        _vuPhase += 0.016;

        if (!_vuUsesLiveLevel)
        {
            if (_vuUsesAnalyzer && _audioLevelAnalyzer.IsReady)
            {
                var position = _usingFallbackPlayer
                    ? _fallbackMediaPlayer.PlaybackSession.Position
                    : TimeSpan.FromSeconds(_vuPhase);
                _vuTargetLevel = MapPcmVuLevel(_audioLevelAnalyzer.ReadLevel(position));
            }
            else
            {
                var bassPulse = Math.Sin(_vuPhase * 3.7) * 0.18;
                var midPulse = Math.Sin(_vuPhase * 7.1 + 0.8) * 0.10;
                var shimmer = Math.Sin(_vuPhase * 13.0 + 1.7) * 0.05;
                var simulated = bassPulse + midPulse + shimmer;
                _vuTargetLevel = Math.Clamp(0.24 + simulated, 0.02, 0.62);
            }
        }

        UpdateVuMeterFrame();
    }

    private void UpdateVuMeterFrame()
    {
        if (VuNeedleTransform is null || VuPeakTextBlock is null || VuLevelTextBlock is null)
        {
            return;
        }

        var targetAlpha = _vuTargetLevel > _vuSmoothedTargetLevel ? 0.30 : 0.08;
        _vuSmoothedTargetLevel = Lerp(_vuSmoothedTargetLevel, _vuTargetLevel, targetAlpha);

        var needleAlpha = _vuSmoothedTargetLevel > _vuDisplayLevel ? 0.18 : 0.055;
        _vuDisplayLevel = Math.Clamp(Lerp(_vuDisplayLevel, _vuSmoothedTargetLevel, needleAlpha), 0.0, 1.0);

        var level = _vuDisplayLevel;
        var angle = -42 + (level * 84);
        VuNeedleTransform.Angle = angle;
        VuLevelTextBlock.Text = $"{level * 100:0}%";
        VuPeakTextBlock.Text = $"{20 * Math.Log10(Math.Max(level, 0.001)):0.0} dB";
        UpdateVuBackgroundVisualizer(level);
    }

    private void UpdateVuBackgroundVisualizer(double level)
    {
        if (VuBar01 is null)
        {
            return;
        }

        var bars = new[]
        {
            VuBar01, VuBar02, VuBar03, VuBar04, VuBar05, VuBar06, VuBar07,
            VuBar08, VuBar09, VuBar10, VuBar11, VuBar12, VuBar13
        };

        for (var i = 0; i < bars.Length; i++)
        {
            var bar = bars[i];
            var wave = (Math.Sin(_vuPhase * (2.2 + (i * 0.13)) + i * 0.72) + 1.0) * 0.5;
            var centerWeight = 1.0 - Math.Abs(i - 6) / 7.5;
            var height = 16 + (level * 96 * centerWeight) + (wave * 22 * level);
            height = Math.Clamp(height, 12, 116);
            Canvas.SetTop(bar, 146 - height);
            bar.Height = height;
            bar.Opacity = Math.Clamp(0.10 + (level * 0.28) + (wave * 0.08), 0.10, 0.48);
        }
    }

    private void ResetVuMeter()
    {
        _vuTargetLevel = 0;
        _vuSmoothedTargetLevel = 0;
        _vuDisplayLevel = 0;
        _vuPhase = 0;
        if (VuNeedleTransform is null)
        {
            return;
        }

        VuNeedleTransform.Angle = -42;
        VuLevelTextBlock.Text = "0%";
        VuPeakTextBlock.Text = "-inf dB";
        UpdateVuBackgroundVisualizer(0);
    }

    private static double Lerp(double from, double to, double amount)
    {
        return from + ((to - from) * Math.Clamp(amount, 0.0, 1.0));
    }

    private static double MapLiveVuLevel(double rms)
    {
        var boosted = Math.Clamp(rms * 6.5, 0.0, 1.0);
        var perceptual = Math.Pow(boosted, 0.42);
        return Math.Clamp(perceptual, 0.02, 0.98);
    }

    private static double MapPcmVuLevel(double rms)
    {
        if (rms <= 0.0001)
        {
            return 0.02;
        }

        var db = 20.0 * Math.Log10(rms);
        var normalized = Math.Clamp((db + 48.0) / 45.0, 0.0, 1.0);
        var vu = Math.Pow(normalized, 1.55);
        return Math.Clamp(vu, 0.02, 0.96);
    }

    private void PrepareVuAnalyzer(string filePath)
    {
        if (DsdExtensions.Contains(Path.GetExtension(filePath)))
        {
            _vuUsesAnalyzer = false;
            return;
        }

        _vuUsesAnalyzer = _audioLevelAnalyzer.Open(filePath);
    }

    private string GetSignalOutputLabel()
    {
        var selectedDevice = GetSelectedOutputDevice();
        var deviceName = selectedDevice is null || selectedDevice.IsSystemDefault
            ? "Windows predeterminado"
            : selectedDevice.Name;
        var path = OutputPathTextBlock?.Text;
        return string.IsNullOrWhiteSpace(path) ? deviceName : $"{path} | {deviceName}";
    }

    private AudioBackend GetSelectedBackend()
    {
        if (BackendComboBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            Enum.TryParse<AudioBackend>(tag, out var backend))
        {
            return backend;
        }

        return AudioBackend.BassWasapi;
    }

    private string GetEffectivePlaybackPath(string path)
    {
        if (_cueEntries.TryGetValue(path, out var cueEntry))
        {
            return cueEntry.AudioPath;
        }

        if (_isoEntries.TryGetValue(path, out var isoEntry))
        {
            return isoEntry.InternalPath;
        }

        return path;
    }

    private async void AddLibraryFolder(string path)
    {
        if (_libraryFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = "La carpeta de biblioteca ya está configurada";
            return;
        }

        _libraryFolders.Add(path);
        LibraryFoldersListView.ItemsSource = null;
        LibraryFoldersListView.ItemsSource = _libraryFolders;
        LibraryFoldersListView.SelectedIndex = _libraryFolders.Count - 1;

        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = $"Escaneando carpeta de música: {path}";

        var tracks = await ScanLibraryFolderAsync(path);
        _libraryTracks.RemoveAll(track => track.Folder.Equals(path, StringComparison.OrdinalIgnoreCase));
        _libraryTracks.AddRange(tracks);
        _libraryTracks.Sort((left, right) => string.Compare(left.Title, right.Title, StringComparison.CurrentCultureIgnoreCase));

        ShowTrackBrowser("Explorador de biblioteca", _libraryTracks);

        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.Message = $"Carpeta agregada: {path} | {tracks.Count} pistas indexadas";

    }

    private static Task<List<LibraryTrack>> ScanLibraryFolderAsync(string folder)
    {
        return Task.Run(() =>
        {
            var tracks = new List<LibraryTrack>();
            if (!Directory.Exists(folder))
            {
                return tracks;
            }

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
                {
                    var extension = Path.GetExtension(filePath);
                    if (!SupportedAudioExtensions.Contains(extension))
                    {
                        continue;
                    }

                    tracks.Add(new LibraryTrack(
                        Path.GetFileNameWithoutExtension(filePath),
                        extension.TrimStart('.').ToUpperInvariant(),
                        extension,
                        filePath,
                        folder));
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }

            return tracks;
        });
    }

    private async Task OpenCueSheetAsync(string cuePath)
    {
        TrackTitleTextBlock.Text = Path.GetFileName(cuePath);
        TrackPathTextBlock.Text = cuePath;
        CodecTextBlock.Text = "CUE";
        TransportBadgeTextBlock.Text = "CUE";
        PlayButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = "Leyendo hoja CUE e indexando pistas virtuales...";

        try
        {
            var entries = await Task.Run(() => CueSheetParser.ParseFile(cuePath));
            if (entries.Count == 0)
            {
                StatusInfoBar.Severity = InfoBarSeverity.Warning;
                StatusInfoBar.Message = "La hoja CUE no apunta a archivos de audio encontrados junto al .cue.";
                return;
            }

            foreach (var key in _cueEntries
                .Where(pair => pair.Value.CuePath.Equals(cuePath, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToList())
            {
                _cueEntries.Remove(key);
            }

            var tracks = new List<LibraryTrack>();
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var virtualPath = $"cue://{cuePath}|{index}";
                _cueEntries[virtualPath] = entry;
                var performer = string.IsNullOrWhiteSpace(entry.Performer) ? string.Empty : $"{entry.Performer} - ";
                tracks.Add(new LibraryTrack(
                    $"{index + 1:00}. {performer}{entry.Title}",
                    $"CUE {Path.GetExtension(entry.AudioPath).TrimStart('.').ToUpperInvariant()}",
                    Path.GetExtension(entry.AudioPath),
                    virtualPath,
                    cuePath));
            }

            ShowTrackBrowser($"CUE: {Path.GetFileNameWithoutExtension(cuePath)}", tracks);
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Message = $"CUE cargado: {tracks.Count} pistas virtuales.";
        }
        catch (Exception ex)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = $"No se pudo leer el CUE: {ex.Message}";
        }
    }

    private void ShowFolderBrowser()
    {
        _visibleLibraryTracks.Clear();
        LibrarySearchBox.Text = string.Empty;
        LibraryBrowserTitleTextBlock.Text = "Carpetas de música";
        LibraryBrowserSubtitleTextBlock.Text = $"{_libraryFolders.Count} carpetas configuradas";
        LibraryBrowserListView.ItemsSource = null;
        LibraryBrowserListView.ItemsSource = _libraryFolders;
    }

    private void ShowTrackBrowser(string title, IEnumerable<LibraryTrack> tracks)
    {
        var visibleTracks = tracks.ToList();
        _activeLibraryTitle = title;
        _visibleLibraryTracks.Clear();
        _visibleLibraryTracks.AddRange(visibleTracks);
        LibrarySearchBox.Text = string.Empty;
        LibraryBrowserTitleTextBlock.Text = title;
        LibraryBrowserSubtitleTextBlock.Text = $"{visibleTracks.Count} pistas";
        LibraryBrowserListView.ItemsSource = null;
        LibraryBrowserListView.ItemsSource = visibleTracks;
    }

    private async Task OpenIsoImageAsync(string isoPath)
    {
        TrackTitleTextBlock.Text = Path.GetFileName(isoPath);
        TrackPathTextBlock.Text = isoPath;
        CodecTextBlock.Text = "ISO";
        TransportBadgeTextBlock.Text = "ISO";
        PlayButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = "Abriendo ISO y buscando contenido musical...";

        try
        {
            var entries = await IsoImageBrowser.ScanAsync(isoPath);
            if (entries.Count == 0)
            {
                await OpenSacdIsoImageAsync(isoPath, "No se encontraron archivos de audio dentro del ISO9660.");
                return;
            }

            _isoEntries.Clear();
            var tracks = new List<LibraryTrack>();
            foreach (var entry in entries)
            {
                var virtualPath = $"iso://{entry.IsoPath}|{entry.InternalPath}";
                _isoEntries[virtualPath] = entry;
                tracks.Add(new LibraryTrack(
                    entry.Title,
                    $"ISO {entry.Extension.TrimStart('.').ToUpperInvariant()}",
                    entry.Extension,
                    virtualPath,
                    entry.IsoPath));
            }

            ShowTrackBrowser($"ISO: {Path.GetFileNameWithoutExtension(isoPath)}", tracks);
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Message = $"ISO abierto: {tracks.Count} archivos de audio encontrados. Doble click para extraer temporalmente y reproducir.";
        }
        catch (Exception ex)
        {
            await OpenSacdIsoImageAsync(isoPath, $"No se pudo leer como ISO9660: {ex.Message}");
        }
    }

    private async Task OpenSacdIsoImageAsync(string isoPath, string reason)
    {
        TrackTitleTextBlock.Text = Path.GetFileName(isoPath);
        TrackPathTextBlock.Text = isoPath;
        CodecTextBlock.Text = "SACD ISO";
        TransportBadgeTextBlock.Text = "EXTRAYENDO";
        PlayButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;

        if (!SacdIsoExtractor.IsAvailable)
        {
            ShowTrackBrowser("Contenido SACD ISO", new[]
            {
                new LibraryTrack(
                    Path.GetFileNameWithoutExtension(isoPath),
                    "SACD ISO",
                    ".iso",
                    isoPath,
                    Path.GetDirectoryName(isoPath) ?? string.Empty)
            });
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = $"{reason} {SacdIsoExtractor.ToolHint}";
            TransportBadgeTextBlock.Text = "SIN EXTRACTOR";
            return;
        }

        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = "SACD ISO detectado. Extrayendo pistas DSF temporales sin conversion PCM ni perdida...";

        try
        {
            var result = await SacdIsoExtractor.ExtractStereoDsfAsync(isoPath);
            var tracks = result.Tracks
                .Select(path => new LibraryTrack(
                    Path.GetFileNameWithoutExtension(path),
                    "SACD DSF",
                    ".dsf",
                    path,
                    result.OutputFolder))
                .ToList();

            ShowTrackBrowser($"SACD ISO: {Path.GetFileNameWithoutExtension(isoPath)}", tracks);
            TransportBadgeTextBlock.Text = "DSF LISTO";
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Message = $"SACD ISO extraido sin perdida: {tracks.Count} pistas DSF temporales listas para reproducir.";
        }
        catch (Exception ex)
        {
            ShowTrackBrowser("Contenido SACD ISO", new[]
            {
                new LibraryTrack(
                    Path.GetFileNameWithoutExtension(isoPath),
                    "SACD ISO",
                    ".iso",
                    isoPath,
                    Path.GetDirectoryName(isoPath) ?? string.Empty)
            });
            TransportBadgeTextBlock.Text = "ERROR";
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = $"No se pudo extraer el SACD ISO sin perdida: {ex.Message}";
        }
    }

    private async Task<string?> ResolvePlayablePathAsync(string path)
    {
        _pendingCueEntry = null;
        if (_cueEntries.TryGetValue(path, out var cueEntry))
        {
            if (!File.Exists(cueEntry.AudioPath))
            {
                StatusInfoBar.Severity = InfoBarSeverity.Warning;
                StatusInfoBar.Message = $"El audio referenciado por el CUE no existe: {cueEntry.AudioPath}";
                return null;
            }

            _pendingCueEntry = cueEntry;
            return cueEntry.AudioPath;
        }

        if (!_isoEntries.TryGetValue(path, out var entry))
        {
            return path;
        }

        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = $"Extrayendo temporalmente desde ISO: {entry.Title}";
        return await IsoImageBrowser.ExtractToTemporaryFileAsync(entry);
    }

    private void ApplyLibrarySearch()
    {
        if (_visibleLibraryTracks.Count == 0)
        {
            return;
        }

        var query = LibrarySearchBox.Text?.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _visibleLibraryTracks
            : _visibleLibraryTracks
                .Where(track =>
                    track.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    track.Format.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    Path.GetFileName(Path.GetDirectoryName(track.Path) ?? string.Empty).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        LibraryBrowserTitleTextBlock.Text = _activeLibraryTitle;
        LibraryBrowserSubtitleTextBlock.Text = string.IsNullOrWhiteSpace(query)
            ? $"{filtered.Count} pistas"
            : $"{filtered.Count} resultados";
        LibraryBrowserListView.ItemsSource = null;
        LibraryBrowserListView.ItemsSource = filtered;
    }

    private void LoadTrack(string filePath)
    {
        _currentFilePath = filePath;
        if (_cueEntries.TryGetValue(filePath, out var cueEntry))
        {
            TrackTitleTextBlock.Text = cueEntry.Title;
            TrackPathTextBlock.Text = $"{Path.GetFileName(cueEntry.CuePath)} > {Path.GetFileName(cueEntry.AudioPath)} @ {FormatTime(cueEntry.Start)}";
            CodecTextBlock.Text = $"CUE {Path.GetExtension(cueEntry.AudioPath).TrimStart('.').ToUpperInvariant()}";
            TransportBadgeTextBlock.Text = "CARGADO";
            UpdateNowPlayingVisuals(cueEntry.AudioPath);
            UpdatePlaybackAvailabilityV2();
            UpdateSignalChain();
            return;
        }

        if (_isoEntries.TryGetValue(filePath, out var isoEntry))
        {
            TrackTitleTextBlock.Text = isoEntry.Title;
            TrackPathTextBlock.Text = $"{Path.GetFileName(isoEntry.IsoPath)} > {isoEntry.InternalPath}";
            CodecTextBlock.Text = $"ISO {isoEntry.Extension.TrimStart('.').ToUpperInvariant()}";
            TransportBadgeTextBlock.Text = "CARGADO";
            UpdateNowPlayingVisuals(isoEntry.IsoPath);
            UpdatePlaybackAvailabilityV2();
            UpdateSignalChain();
            return;
        }

        TrackTitleTextBlock.Text = Path.GetFileName(filePath);
        TrackPathTextBlock.Text = filePath;
        CodecTextBlock.Text = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
        TransportBadgeTextBlock.Text = "CARGADO";
        UpdateNowPlayingVisuals(filePath);
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
    }

    private async Task PlayNextTrackAsync(bool manual)
    {
        var nextTrack = GetAdjacentTrack(direction: 1, allowShuffle: _shuffleEnabled);
        if (nextTrack is null)
        {
            if (manual)
            {
                StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = "No hay otra pista disponible. Agrega una carpeta a la biblioteca para usar siguiente/aleatorio.";
            }

            return;
        }

        StopCurrentPlaybackForTrackChange();
        LoadTrack(nextTrack.Path);
        await Task.Delay(80);
        PlayButton_Click(this, null!);
    }

    private async Task PlayPreviousTrackAsync(bool manual)
    {
        var previousTrack = GetAdjacentTrack(direction: -1, allowShuffle: false);
        if (previousTrack is null)
        {
            if (manual)
            {
                StatusInfoBar.Severity = InfoBarSeverity.Informational;
                StatusInfoBar.Message = "No hay pista anterior disponible en la lista actual.";
            }

            return;
        }

        StopCurrentPlaybackForTrackChange();
        LoadTrack(previousTrack.Path);
        await Task.Delay(80);
        PlayButton_Click(this, null!);
    }

    private LibraryTrack? GetAdjacentTrack(int direction, bool allowShuffle)
    {
        var queueSource = _visibleLibraryTracks.Count > 0 ? _visibleLibraryTracks : _libraryTracks;
        if (queueSource.Count == 0)
        {
            return null;
        }

        var playableTracks = queueSource
            .Where(track => WindowsFallbackExtensions.Contains(track.Extension) ||
                track.Extension.Equals(".dsf", StringComparison.OrdinalIgnoreCase) ||
                track.Path.StartsWith("iso://", StringComparison.OrdinalIgnoreCase) ||
                IsNativeBackendAvailable(GetSelectedBackend(), GetEffectivePlaybackPath(track.Path)))
            .ToList();
        if (playableTracks.Count == 0)
        {
            return null;
        }

        if (playableTracks.Count == 1)
        {
            return playableTracks[0].Path.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase)
                ? null
                : playableTracks[0];
        }

        if (allowShuffle)
        {
            LibraryTrack candidate;
            do
            {
                candidate = playableTracks[_shuffleRandom.Next(playableTracks.Count)];
            }
            while (candidate.Path.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase));

            return candidate;
        }

        var currentIndex = playableTracks.FindIndex(track => track.Path.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
        {
            return direction > 0 ? playableTracks[0] : playableTracks[^1];
        }

        var nextIndex = (currentIndex + direction) % playableTracks.Count;
        if (nextIndex < 0)
        {
            nextIndex += playableTracks.Count;
        }

        return playableTracks[nextIndex];
    }

    private void StopCurrentPlaybackForTrackChange()
    {
        if (_usingFallbackPlayer)
        {
            _fallbackMediaPlayer.Pause();
            _fallbackMediaPlayer.Source = null;
        }
        else
        {
            _audioEngine.Stop();
        }

        _playbackTimer.Stop();
        _vuTimer.Stop();
        _vuUsesAnalyzer = false;
        _vuUsesLiveLevel = false;
        ResetVuMeter();
        _isPaused = false;
    }

    private void UpdateShuffleButtonVisual()
    {
        if (ShuffleButton is null)
        {
            return;
        }

        ShuffleButton.Opacity = _shuffleEnabled ? 1.0 : 0.45;
    }

    private async Task PlayWithWindowsFallbackAsync(string filePath)
    {
        CleanupTemporaryFallbackFile();

        var playbackPath = filePath;
        MediaSource? playbackSource = null;
        if (DsdExtensions.Contains(Path.GetExtension(filePath)))
        {
            var dsdExtension = Path.GetExtension(filePath);
            if (dsdExtension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SACD ISO requiere extraccion previa a DSF. Abre el ISO desde la biblioteca para extraer pistas temporales.");
            }

            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = "DSD nativo no disponible. Streaming DSD -> PCM en RAM para Realtek/Windows, sin cache en disco.";
            TransportBadgeTextBlock.Text = "DSD -> PCM";
            _vuUsesLiveLevel = true;
            _vuUsesAnalyzer = false;
            playbackSource = DsfPcmStreamSource.CreateMediaSource(filePath, _toneSettings, level => _vuTargetLevel = MapLiveVuLevel(level));
        }
        else if (Path.GetExtension(filePath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            _vuUsesLiveLevel = true;
            _vuUsesAnalyzer = false;
            playbackSource = WavPcmStreamSource.CreateMediaSource(filePath, level => _vuTargetLevel = MapLiveVuLevel(level));
        }
        else
        {
            _vuUsesLiveLevel = false;
            PrepareVuAnalyzer(filePath);
        }

        if (playbackSource is null && !WindowsFallbackExtensions.Contains(Path.GetExtension(playbackPath)))
        {
            throw new InvalidOperationException("Windows fallback no puede garantizar soporte para este formato. Instala MPV o BASS para reproducirlo.");
        }

        if (playbackSource is null)
        {
            var file = await StorageFile.GetFileFromPathAsync(playbackPath);
            playbackSource = MediaSource.CreateFromStorageFile(file);
        }

        _fallbackMediaPlayer.AudioDevice = GetSelectedOutputDevice()?.Device;
        _fallbackMediaPlayer.Source = playbackSource;
        _fallbackMediaPlayer.Play();
        _usingFallbackPlayer = true;
        StartVuMeter();
        OutputPathTextBlock.Text = DsdExtensions.Contains(Path.GetExtension(filePath)) ? "DSD en RAM" : "Fallback de Windows";
        if (DsdExtensions.Contains(Path.GetExtension(filePath)))
        {
            SampleRateTextBlock.Text = "88.2 kHz";
            BitDepthTextBlock.Text = "16 bit";
            ChannelsTextBlock.Text = "2";
            BitrateTextBlock.Text = "-- kbps";
            CodecTextBlock.Text = $"{Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant()} -> PCM";
            UpdateSignalChain("Decodificación DSF en stream");
        }
        else
        {
            UpdateSignalChain(Path.GetExtension(filePath).Equals(".wav", StringComparison.OrdinalIgnoreCase)
                ? "WAV PCM stream"
                : "Windows Media Foundation");
        }

        WritePlaybackAuditLog(filePath, "Windows fallback");
    }

    private void CleanupTemporaryFallbackFile()
    {
        if (string.IsNullOrWhiteSpace(_temporaryFallbackFilePath))
        {
            return;
        }

        _temporaryFallbackFilePath = null;
    }

    private void WritePlaybackAuditLog(string filePath, string route)
    {
        try
        {
            var auditDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZenithAudio",
                "Audit");
            Directory.CreateDirectory(auditDirectory);

            var line =
                $"{DateTimeOffset.Now:O}\t" +
                $"Ruta={route}\t" +
                $"Archivo={filePath}\t" +
                $"Backend={BackendTextBlock.Text}\t" +
                $"Modo={ModeTextBlock.Text}\t" +
                $"Salida={OutputPathTextBlock.Text}\t" +
                $"Buffer={(int)(BufferSlider?.Value ?? 100)}ms\t" +
                $"PerfilBuffer={_bufferProfile}\t" +
                $"DSP={SignalDspTextBlock.Text}\t" +
                $"Dither={SignalDitherTextBlock.Text}\t" +
                $"BitPerfect={SignalBitPerfectTextBlock.Text}";

            File.AppendAllText(
                Path.Combine(auditDirectory, "playback-audit.tsv"),
                line + Environment.NewLine,
                Encoding.UTF8);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void CleanupAllTemporaryFallbackFiles()
    {
        _fallbackMediaPlayer.Pause();
        _fallbackMediaPlayer.Source = null;
        CleanupTemporaryFallbackFile();
        IsoImageBrowser.CleanupTemporaryFiles();
        SacdIsoExtractor.CleanupTemporaryFiles();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        CleanupAllTemporaryFallbackFiles();
        _audioEngine.Dispose();
        _audioLevelAnalyzer.Dispose();
    }

    private static bool IsNativeBackendAvailable(AudioBackend backend, string filePath)
    {
        if (backend == AudioBackend.MpvWasapi)
        {
            return IsDllAvailable("mpv-2.dll");
        }

        if (!IsDllAvailable("bass.dll") || !IsDllAvailable("basswasapi.dll"))
        {
            return false;
        }

        var extension = Path.GetExtension(filePath);
        if (DsdExtensions.Contains(extension))
        {
            return IsDllAvailable("bassdsd.dll");
        }

        if (extension.Equals(".ape", StringComparison.OrdinalIgnoreCase))
        {
            return IsDllAvailable("bass_ape.dll");
        }

        if (extension.Equals(".wv", StringComparison.OrdinalIgnoreCase))
        {
            return IsDllAvailable("bass_wv.dll");
        }

        if (extension.Equals(".opus", StringComparison.OrdinalIgnoreCase))
        {
            return IsDllAvailable("bassopus.dll") || WindowsFallbackExtensions.Contains(extension);
        }

        return true;
    }

    private static bool IsDllAvailable(string fileName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var nativeDirectory = Path.Combine(baseDirectory, "runtimes", "win-x64", "native");

        if (File.Exists(Path.Combine(baseDirectory, fileName)) ||
            File.Exists(Path.Combine(nativeDirectory, fileName)))
        {
            return true;
        }

        try
        {
            if (NativeLibrary.TryLoad(fileName, out var handle))
            {
                NativeLibrary.Free(handle);
                return true;
            }
        }
        catch (BadImageFormatException)
        {
        }
        catch (DllNotFoundException)
        {
        }

        return false;
    }

    private void ApplyWindowsMaximumQualityFallback(AudioBackend backend, string reason)
    {
        PreferredFormatComboBox.SelectedIndex = 2;
        ExclusiveModeToggle.IsOn = false;
        ModeTextBlock.Text = "Windows max";
        OutputPathTextBlock.Text = "Calidad maxima Windows";

        StatusInfoBar.Severity = InfoBarSeverity.Warning;
        StatusInfoBar.Message = backend == AudioBackend.MpvWasapi
            ? $"{reason}. Zenith usará automáticamente la calidad máxima que Windows pueda negociar."
            : $"{reason}. Zenith usará el reproductor de Windows con la mayor calidad soportada.";
    }

    private void ApplyOutputDeviceSettings()
    {
        if (OutputDeviceComboBox is null || ExclusiveModeToggle is null || OutputPathTextBlock is null || ModeTextBlock is null)
        {
            return;
        }

        var selectedDevice = GetSelectedOutputDevice();
        if (selectedDevice is null || selectedDevice.IsSystemDefault)
        {
            if (ExclusiveModeToggle.IsOn)
            {
                ExclusiveModeToggle.IsOn = false;
            }

            PreferredFormatComboBox.SelectedIndex = 2;
            OutputPathTextBlock.Text = "Windows predeterminado";
            ModeTextBlock.Text = "Windows max";
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = "Usando el dispositivo predeterminado de Windows. Para modo exclusivo elige un endpoint especifico como Realtek, USB o DAC.";
            return;
        }

        _fallbackMediaPlayer.AudioDevice = selectedDevice.Device;

        OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn
            ? $"{selectedDevice.KindLabel} exclusivo"
            : $"{selectedDevice.KindLabel} compartido";
        ModeTextBlock.Text = ExclusiveModeToggle.IsOn ? "Bit-perfect" : "Windows max";
        StatusInfoBar.Severity = selectedDevice.IsLikelyDac || selectedDevice.IsUsb ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        StatusInfoBar.Message = ExclusiveModeToggle.IsOn
            ? $"Salida seleccionada: {selectedDevice.Name}. Zenith intentará WASAPI exclusivo si el backend nativo está disponible."
            : $"Salida seleccionada: {selectedDevice.Name}. Windows negociara la mejor calidad compatible.";
    }

    private bool IsExclusiveDacSelected()
    {
        var selectedDevice = GetSelectedOutputDevice();
        return selectedDevice is not null && !selectedDevice.IsSystemDefault && (selectedDevice.IsLikelyDac || selectedDevice.IsUsb);
    }

    private OutputDeviceOption? GetSelectedOutputDevice()
    {
        return OutputDeviceComboBox?.SelectedItem as OutputDeviceOption;
    }

    private void ShowNativeDsdMissingAlert(AudioBackend backend)
    {
        StopButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        StatusInfoBar.Severity = InfoBarSeverity.Warning;
        StatusInfoBar.Message = GetNativeDsdMissingMessage(backend);
        OutputPathTextBlock.Text = "DSD nativo requerido";
    }

    private static string GetNativeDsdMissingMessage(AudioBackend backend)
    {
        return "DSD nativo requiere mpv-2.dll o bass.dll + basswasapi.dll + bassdsd.dll. Sin DAC/librerías, Zenith bajará DSF a PCM por Windows cuando sea posible.";
    }

    private void UpdatePlaybackAvailability()
    {
        if (StatusInfoBar is null || PlayButton is null || ModeTextBlock is null || OutputPathTextBlock is null)
        {
            return;
        }

        var backend = GetSelectedBackend();
        var hasTrack = !string.IsNullOrWhiteSpace(_currentFilePath);
        var effectivePath = hasTrack ? GetEffectivePlaybackPath(_currentFilePath!) : string.Empty;
        var extension = hasTrack ? Path.GetExtension(effectivePath) : string.Empty;
        var isDsd = DsdExtensions.Contains(extension);
        var nativeAvailable = hasTrack && IsNativeBackendAvailable(backend, effectivePath);

        BackendTextBlock.Text = backend == AudioBackend.BassWasapi ? "BASS" : "MPV";

        if (!hasTrack)
        {
            PlayButton.IsEnabled = false;
            ModeTextBlock.Text = ExclusiveModeToggle.IsOn ? "Bit-perfect" : "Compartido";
            OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn ? "DAC exclusivo" : "Windows compartido";
            return;
        }

        if (nativeAvailable)
        {
            PlayButton.IsEnabled = true;

            if (backend == AudioBackend.MpvWasapi && !IsExclusiveDacSelected())
            {
                PreferredFormatComboBox.SelectedIndex = 2;
                ExclusiveModeToggle.IsOn = false;
                ModeTextBlock.Text = "Windows max";
                OutputPathTextBlock.Text = "MPV compartido max";
                StatusInfoBar.Severity = InfoBarSeverity.Warning;
                StatusInfoBar.Message = "MPV está disponible, pero no hay DAC exclusivo seleccionado. Se usará salida compartida con la mejor calidad negociada por Windows.";
                return;
            }

            ModeTextBlock.Text = ExclusiveModeToggle.IsOn ? "Bit-perfect" : "Compartido";
            OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn ? "DAC exclusivo" : "Windows compartido";
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Message = "Pista lista para reproducir";
            return;
        }

        if (isDsd)
        {
            var canConvertDsd = extension.Equals(".dsf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".dff", StringComparison.OrdinalIgnoreCase);
            PlayButton.IsEnabled = canConvertDsd;
            PreferredFormatComboBox.SelectedIndex = 2;
            ExclusiveModeToggle.IsOn = false;
            ModeTextBlock.Text = canConvertDsd ? "DSD a PCM" : "DSD nativo";
            OutputPathTextBlock.Text = canConvertDsd ? "PCM Realtek/Windows" : "DLL nativas faltantes";
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = canConvertDsd
                ? "No hay DAC/librerías nativas. Zenith convertirá DSF a PCM 88.2 kHz y reproducirá por Windows/Realtek."
                : GetNativeDsdMissingMessage(backend);
            return;
        }

        PlayButton.IsEnabled = WindowsFallbackExtensions.Contains(extension);
        PreferredFormatComboBox.SelectedIndex = 2;
        ExclusiveModeToggle.IsOn = false;
        ModeTextBlock.Text = "Windows max";
        OutputPathTextBlock.Text = "Fallback de Windows";
        StatusInfoBar.Severity = InfoBarSeverity.Warning;
        StatusInfoBar.Message = backend == AudioBackend.MpvWasapi && !IsDllAvailable("mpv-2.dll")
            ? "MPV no está instalado. Se usará Windows fallback con la mayor calidad que el sistema pueda negociar."
            : "BASS no está instalado. Se usará Windows fallback con la mayor calidad que el sistema pueda negociar.";
    }

    private void UpdatePlaybackAvailabilityV2()
    {
        if (StatusInfoBar is null || PlayButton is null || ModeTextBlock is null || OutputPathTextBlock is null)
        {
            return;
        }

        RefreshBackendLabels();

        var backend = GetSelectedBackend();
        var hasTrack = !string.IsNullOrWhiteSpace(_currentFilePath);
        var effectivePath = hasTrack ? GetEffectivePlaybackPath(_currentFilePath!) : string.Empty;
        var extension = hasTrack ? Path.GetExtension(effectivePath) : string.Empty;
        var isDsd = DsdExtensions.Contains(extension);
        var nativeAvailable = hasTrack && IsNativeBackendAvailable(backend, effectivePath);

        BackendTextBlock.Text = backend == AudioBackend.BassWasapi ? "BASS" : "MPV";

        if (!hasTrack)
        {
            PlayButton.IsEnabled = false;
            ModeTextBlock.Text = ExclusiveModeToggle.IsOn ? "Bit-perfect" : "Compartido";
            OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn ? "DAC exclusivo" : "Windows compartido";
            return;
        }

        if (nativeAvailable)
        {
            PlayButton.IsEnabled = true;

            if (backend == AudioBackend.MpvWasapi && !IsExclusiveDacSelected())
            {
                PreferredFormatComboBox.SelectedIndex = 2;
                ExclusiveModeToggle.IsOn = false;
                ModeTextBlock.Text = "Windows max";
                OutputPathTextBlock.Text = "MPV compartido max";
                StatusInfoBar.Severity = InfoBarSeverity.Warning;
                StatusInfoBar.Message = "MPV está disponible, pero no hay DAC exclusivo seleccionado. Se usará salida compartida con la mejor calidad negociada por Windows.";
                return;
            }

            ModeTextBlock.Text = ExclusiveModeToggle.IsOn ? "Bit-perfect" : "Compartido";
            OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn ? "DAC exclusivo" : "Windows compartido";
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Message = "Pista lista para reproducir";
            return;
        }

        if (isDsd)
        {
            var canConvertDsd = extension.Equals(".dsf", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".dff", StringComparison.OrdinalIgnoreCase);
            PlayButton.IsEnabled = canConvertDsd;
            PreferredFormatComboBox.SelectedIndex = 2;
            ExclusiveModeToggle.IsOn = false;
            ModeTextBlock.Text = canConvertDsd ? "DSD a PCM" : "DSD nativo";
            OutputPathTextBlock.Text = canConvertDsd ? "PCM Realtek/Windows" : "DLL nativas faltantes";
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = canConvertDsd
                ? "No hay DAC/librerías nativas. Zenith convertirá DSF a PCM 88.2 kHz y reproducirá por Windows/Realtek."
                : GetNativeDsdMissingMessage(backend);
            return;
        }

        PlayButton.IsEnabled = WindowsFallbackExtensions.Contains(extension);
        PreferredFormatComboBox.SelectedIndex = 2;
        ExclusiveModeToggle.IsOn = false;
        ModeTextBlock.Text = "Windows max";
        OutputPathTextBlock.Text = "Fallback de Windows";
        StatusInfoBar.Severity = InfoBarSeverity.Warning;
        StatusInfoBar.Message = backend == AudioBackend.MpvWasapi && !IsDllAvailable("mpv-2.dll")
            ? "MPV no está instalado. Se usará Windows fallback con la mayor calidad que el sistema pueda negociar."
            : "BASS no está instalado. Se usará Windows fallback con la mayor calidad que el sistema pueda negociar.";
    }

    private void RefreshBackendLabels()
    {
        if (BackendComboBox is null || BackendComboBox.Items.Count < 2)
        {
            return;
        }

        if (BackendComboBox.Items[0] is ComboBoxItem bassItem)
        {
            bassItem.Content = IsDllAvailable("bass.dll") && IsDllAvailable("basswasapi.dll")
                ? "BASS WASAPI"
                : "BASS WASAPI (opcional)";
        }

        if (BackendComboBox.Items[1] is ComboBoxItem mpvItem)
        {
            mpvItem.Content = IsDllAvailable("mpv-2.dll")
                ? "MPV WASAPI"
                : "MPV WASAPI (opcional)";
        }
    }

    private bool ShouldConvertDsdToPcm()
    {
        return DsdModeComboBox?.SelectedIndex == 3;
    }

    private string FormatZenithAiTranscript(string? pendingMessage = null)
    {
        if (_zenithAiMessages.Count == 0 && pendingMessage is null)
        {
            return "ZenitAI (BETA) listo. Pregúntame por la pista actual, diferencias entre DSD/FLAC/PCM, historia de un álbum, configuración de Windows, DACs o escucha crítica.";
        }

        var lines = _zenithAiMessages
            .Select(message =>
            {
                var speaker = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                    ? "ZenitAI"
                    : "Tu";
                return $"{speaker}: {message.Content}";
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(pendingMessage))
        {
            lines.Add(pendingMessage);
        }

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", lines);
    }

    private void ScrollZenithAiTranscriptToEnd(ScrollViewer scrollViewer)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            scrollViewer.UpdateLayout();
            scrollViewer.ChangeView(null, scrollViewer.ScrollableHeight, null, disableAnimation: false);
        });
    }

    private string BuildZenithAiAudioContext()
    {
        var selectedOutput = GetSelectedOutputDevice()?.ToString() ?? "Salida predeterminada de Windows";
        var currentTrack = string.IsNullOrWhiteSpace(_currentFilePath)
            ? "Sin pista cargada"
            : $"{TrackTitleTextBlock.Text} ({_currentFilePath})";

        return
            $"Pista actual: {currentTrack}. " +
            $"Codec: {CodecTextBlock.Text}. " +
            $"Bitrate: {BitrateTextBlock.Text}. " +
            $"Frecuencia: {SampleRateTextBlock.Text}. " +
            $"Profundidad: {BitDepthTextBlock.Text}. " +
            $"Canales: {ChannelsTextBlock.Text}. " +
            $"Backend seleccionado: {BackendTextBlock.Text}. " +
            $"Modo: {ModeTextBlock.Text}. " +
            $"Ruta de salida: {OutputPathTextBlock.Text}. " +
            $"Dispositivo: {selectedOutput}. " +
            $"Cadena de señal: fuente {SignalSourceTextBlock.Text}, decodificación {SignalDecodeTextBlock.Text}, DSP {SignalDspTextBlock.Text}, salida {SignalOutputTextBlock.Text}. " +
            $"Control de tono: {ToneSummaryTextBlock.Text}.";
    }

    private static string FormatTime(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return "0:00";
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
    }

    private static string FormatDb(double value)
    {
        return value >= 0 ? $"+{value:0.#} dB" : $"{value:0.#} dB";
    }

    private static string FormatPlaybackState(PlaybackState state)
    {
        return state switch
        {
            PlaybackState.Initializing => "Inicializando",
            PlaybackState.Ready => "Listo",
            PlaybackState.Playing => "Reproduciendo",
            PlaybackState.Paused => "Pausado",
            PlaybackState.Stopped => "Detenido",
            PlaybackState.Error => "Error",
            _ => state.ToString()
        };
    }

    private sealed record OutputDeviceOption(string Name, string Id, DeviceInformation? Device, bool IsSystemDefault, bool IsUsb, bool IsLikelyDac)
    {
        public static OutputDeviceOption SystemDefault { get; } = new(
            "Salida predeterminada de Windows",
            string.Empty,
            null,
            true,
            false,
            false);

        public string KindLabel => IsSystemDefault
            ? "Windows"
            : IsLikelyDac ? "DAC"
            : IsUsb ? "USB"
            : "WASAPI";

        public static OutputDeviceOption FromDevice(DeviceInformation device)
        {
            var name = string.IsNullOrWhiteSpace(device.Name) ? "Dispositivo de salida de audio" : device.Name;
            var normalizedName = name.ToLowerInvariant();
            var normalizedId = device.Id.ToLowerInvariant();
            var isUsb = normalizedName.Contains("usb", StringComparison.Ordinal) || normalizedId.Contains("usb", StringComparison.Ordinal);
            var isLikelyDac =
                normalizedName.Contains("dac", StringComparison.Ordinal) ||
                normalizedName.Contains("asio", StringComparison.Ordinal) ||
                normalizedName.Contains("ifi", StringComparison.Ordinal) ||
                normalizedName.Contains("topping", StringComparison.Ordinal) ||
                normalizedName.Contains("fiio", StringComparison.Ordinal) ||
                normalizedName.Contains("smsl", StringComparison.Ordinal) ||
                normalizedName.Contains("focusrite", StringComparison.Ordinal) ||
                normalizedName.Contains("motu", StringComparison.Ordinal) ||
                normalizedName.Contains("scarlett", StringComparison.Ordinal);

            return new OutputDeviceOption(name, device.Id, device, false, isUsb, isLikelyDac);
        }

        public override string ToString()
        {
            return IsSystemDefault ? Name : $"{Name}  |  {KindLabel}";
        }
    }

    private sealed record LibraryTrack(string Title, string Format, string Extension, string Path, string Folder)
    {
        public override string ToString()
        {
            return $"{Title}  |  {Format}";
        }
    }

    private sealed record SyncedLyricLine(TimeSpan Time, string Text);
}
