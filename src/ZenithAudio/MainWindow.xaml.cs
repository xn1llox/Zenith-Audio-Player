using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
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
        ".m4a",
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
        ".m4a",
        ".ogg",
        ".opus"
    };

    private readonly AudioEngine _audioEngine = new();
    private readonly WasapiPlaybackEngine _wasapiEngine = new();
    private readonly AsioPlaybackEngine _asioEngine = new();
    private readonly AudioLevelAnalyzer _audioLevelAnalyzer = new();
    private readonly ToneControlSettings _toneSettings = new();
    private readonly List<AsioDriverInfo> _asioDrivers = [];
    private bool _usingWasapiEngine;
    private bool _usingAsioEngine;
    private bool _isLoadingAsioDrivers;
    private bool _isSyncingOutputSelectors;
    private bool _isLoadingAudioSettings;
    private bool _isRevertingExclusiveToggle;
    private bool _isShowingExclusiveAlert;
    private bool _showInspectorInNarrow;
    private int _compactPaneMode; // 0 = Player, 1 = Library, 2 = Inspector
    private double _lastLayoutWidth;
    private double _lastLayoutHeight;
    private string? _activePlaybackFilePath;
    private SavedAudioSettings? _savedAudioSettings;
    private readonly ZenithAiClient _zenithAiClient = new();
    private readonly List<ZenithAiChatMessage> _zenithAiMessages = [];
    private readonly List<ZenithAiModelInfo> _zenithAiFetchedModels = [];
    private CancellationTokenSource? _zenithAiRequestCancellation;
    private bool _zenithAiCancelRequestedByUser;
    private bool _isSyncingZenithAiProviderPreset;
    private bool _isDraggingZenithAiPanel;
    private Windows.Foundation.Point _zenithAiDragStartPoint;
    private double _zenithAiDragStartX;
    private double _zenithAiDragStartY;
    private readonly List<string> _libraryFolders = [];
    private readonly List<LibraryTrack> _libraryTracks = [];
    private readonly List<LibraryTrack> _visibleLibraryTracks = [];
    private readonly Dictionary<string, IsoAudioEntry> _isoEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CueAudioEntry> _cueEntries = new(StringComparer.OrdinalIgnoreCase);
    private string _activeLibraryTitle = "Explorador de biblioteca";
    private bool _isShowingFolderBrowser;
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
    private Windows.UI.Color _dynamicAccentColor = GetDefaultOledBlueAccent();

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
        LibraryBrowserListView.ItemsSource = Array.Empty<LibraryBrowserItem>();
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
        _wasapiEngine.TrackEnded += WasapiEngine_TrackEnded;
        _wasapiEngine.LevelChanged += WasapiEngine_LevelChanged;
        _asioEngine.TrackEnded += AsioEngine_TrackEnded;
        _asioEngine.LevelChanged += AsioEngine_LevelChanged;
        FallbackPlayerElement.SetMediaPlayer(_fallbackMediaPlayer);
        _fallbackMediaPlayer.MediaOpened += FallbackMediaPlayer_MediaOpened;
        _fallbackMediaPlayer.MediaEnded += FallbackMediaPlayer_MediaEnded;
        _fallbackMediaPlayer.AudioCategory = MediaPlayerAudioCategory.Media;
        _fallbackMediaPlayer.Volume = 1.0;
        Closed += MainWindow_Closed;
        LoadSavedAudioSettings();
        InitializeOutputDeviceList();
        RefreshAsioDrivers();
        UpdateSacdExtractorStatus();
        UpdateToneControlState(showStatus: false);
        ApplyOutputDeviceSettings();
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
        _vuUsesLiveLevel = false;
        ResetVuMeter();
        UpdateShuffleButtonVisual();
        ConfigureDarkTitleBar();
        SetWindowIcon();
        LoadBrandLogos();
        SelectRightPanelTab(0);
        if (ZenithAiProviderPresetComboBox is not null)
        {
            ZenithAiProviderPresetComboBox.ItemsSource = ZenithAiClient.ProviderPresets;
        }
        InitializeRemoteControl();
        _ = RefreshOutputDevicesAsync();
        _ = LoadSavedLibraryFoldersAsync();
    }

    private void ConfigureDarkTitleBar()
    {
        try
        {
            var titleBar = AppWindow.TitleBar;
            if (titleBar is null)
            {
                return;
            }

            var bg = Microsoft.UI.ColorHelper.FromArgb(255, 2, 6, 11);
            var fg = Microsoft.UI.ColorHelper.FromArgb(255, 224, 242, 255);
            var muted = Microsoft.UI.ColorHelper.FromArgb(255, 130, 168, 196);
            var hoverBg = Microsoft.UI.ColorHelper.FromArgb(255, 14, 42, 66);
            var pressedBg = Microsoft.UI.ColorHelper.FromArgb(255, 22, 66, 102);

            titleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.ShowIconAndSystemMenu;
            titleBar.BackgroundColor = bg;
            titleBar.ForegroundColor = fg;
            titleBar.InactiveBackgroundColor = bg;
            titleBar.InactiveForegroundColor = muted;
            titleBar.ButtonBackgroundColor = bg;
            titleBar.ButtonForegroundColor = fg;
            titleBar.ButtonHoverBackgroundColor = hoverBg;
            titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.White;
            titleBar.ButtonPressedBackgroundColor = pressedBg;
            titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.White;
            titleBar.ButtonInactiveBackgroundColor = bg;
            titleBar.ButtonInactiveForegroundColor = muted;
        }
        catch
        {
        }
    }

    private void RightTabSignalButton_Click(object sender, RoutedEventArgs e)
    {
        SelectRightPanelTab(0);
    }

    private void RightTabToneButton_Click(object sender, RoutedEventArgs e)
    {
        SelectRightPanelTab(1);
    }

    private void RightTabSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SelectRightPanelTab(2);
    }

    private void SelectRightPanelTab(int tabIndex)
    {
        if (SystemSettingsBorder is null || ToneControlCardBorder is null || SystemConfigCardBorder is null)
        {
            return;
        }

        SystemSettingsBorder.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        ToneControlCardBorder.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        SystemConfigCardBorder.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

        if (CreditsCardBorder is not null)
        {
            CreditsCardBorder.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        UpdateRightTabButtonStyle(RightTabSignalButton, isActive: tabIndex == 0);
        UpdateRightTabButtonStyle(RightTabToneButton, isActive: tabIndex == 1);
        UpdateRightTabButtonStyle(RightTabSettingsButton, isActive: tabIndex == 2);

        RightPanelScrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
    }

    private static void UpdateRightTabButtonStyle(Button? button, bool isActive)
    {
        if (button is null)
        {
            return;
        }

        button.Background = new SolidColorBrush(isActive
            ? Microsoft.UI.ColorHelper.FromArgb(255, 8, 56, 92)
            : Microsoft.UI.ColorHelper.FromArgb(255, 4, 10, 18));
        button.BorderBrush = new SolidColorBrush(isActive
            ? Microsoft.UI.ColorHelper.FromArgb(255, 56, 178, 248)
            : Microsoft.UI.ColorHelper.FromArgb(255, 18, 46, 70));
        button.Foreground = new SolidColorBrush(isActive
            ? Microsoft.UI.ColorHelper.FromArgb(255, 232, 247, 255)
            : Microsoft.UI.ColorHelper.FromArgb(255, 152, 184, 208));
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width, e.NewSize.Height);
    }

    private void ToggleLibraryPaneButton_Click(object sender, RoutedEventArgs e)
    {
        _compactPaneMode = _compactPaneMode == 1 ? 0 : 1;
        ApplyResponsiveLayout(
            _lastLayoutWidth > 0 ? _lastLayoutWidth : RootGrid.ActualWidth,
            _lastLayoutHeight > 0 ? _lastLayoutHeight : RootGrid.ActualHeight);
    }

    private void ToggleInspectorPaneButton_Click(object sender, RoutedEventArgs e)
    {
        var width = _lastLayoutWidth > 0 ? _lastLayoutWidth : RootGrid.ActualWidth;
        if (width < 780)
        {
            _compactPaneMode = _compactPaneMode == 2 ? 0 : 2;
        }
        else
        {
            _showInspectorInNarrow = !_showInspectorInNarrow;
        }

        ApplyResponsiveLayout(width, _lastLayoutHeight > 0 ? _lastLayoutHeight : RootGrid.ActualHeight);
    }

    private void ApplyResponsiveLayout(double width, double height)
    {
        if (width <= 0 || height <= 0 || TopHeaderGrid is null || WorkspaceGrid is null || PlaybackBarGrid is null)
        {
            return;
        }

        _lastLayoutWidth = width;
        _lastLayoutHeight = height;

        var isWide = width >= 1120;
        var isHalfScreen = width >= 780 && width < 1120;
        var isCompact = width < 780;
        var isShort = height < 730;

        // 1. Global spacing & padding density
        if (isCompact || isShort)
        {
            RootGrid.Padding = new Thickness(10, 8, 10, 10);
            RootGrid.RowSpacing = 8;
            TopHeaderBorder.Padding = new Thickness(12, 8, 12, 8);
            MainListeningPanelBorder.Padding = new Thickness(14, 12, 14, 12);
            PlaybackBarBorder.Padding = new Thickness(12, 8, 12, 8);
        }
        else if (width < 1340)
        {
            RootGrid.Padding = new Thickness(12, 10, 12, 12);
            RootGrid.RowSpacing = 10;
            TopHeaderBorder.Padding = new Thickness(14, 9, 14, 9);
            MainListeningPanelBorder.Padding = new Thickness(16, 14, 16, 14);
            PlaybackBarBorder.Padding = new Thickness(14, 9, 14, 9);
        }
        else
        {
            RootGrid.Padding = new Thickness(16, 12, 16, 14);
            RootGrid.RowSpacing = 12;
            TopHeaderBorder.Padding = new Thickness(16, 10, 16, 10);
            MainListeningPanelBorder.Padding = new Thickness(20, 18, 20, 18);
            PlaybackBarBorder.Padding = new Thickness(16, 10, 16, 10);
        }

        // 2. Adaptive Top Header (1-row on wide screens, 2-row on split/compact)
        var twoRowHeader = width < 1240;
        if (twoRowHeader)
        {
            Grid.SetRow(AudioOutputBarBorder, 1);
            Grid.SetColumn(AudioOutputBarBorder, 0);
            Grid.SetColumnSpan(AudioOutputBarBorder, 3);
            AudioOutputBarBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
            QuickOutputDeviceComboBox.Width = double.NaN;
            BackendComboBox.Width = width < 640 ? 128 : 158;
            BrandSubtitleTextBlock.Visibility = Visibility.Collapsed;
            BrandLogoImage.Width = width < 640 ? 30 : 34;
            BrandLogoImage.Height = BrandLogoImage.Width;
            BrandTitleTextBlock.FontSize = width < 640 ? 17 : 19;
            ExclusiveLabelTextBlock.Visibility = width < 620 ? Visibility.Collapsed : Visibility.Visible;
            OutputBarDivider.Visibility = width < 620 ? Visibility.Collapsed : Visibility.Visible;
            OpenFileLabelTextBlock.Visibility = width < 700 ? Visibility.Collapsed : Visibility.Visible;
            ZenithAiLabelTextBlock.Visibility = width < 700 ? Visibility.Collapsed : Visibility.Visible;
            if (RemoteLabelTextBlock is not null)
            {
                RemoteLabelTextBlock.Visibility = width < 760 ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        else
        {
            Grid.SetRow(AudioOutputBarBorder, 0);
            Grid.SetColumn(AudioOutputBarBorder, 1);
            Grid.SetColumnSpan(AudioOutputBarBorder, 1);
            AudioOutputBarBorder.HorizontalAlignment = HorizontalAlignment.Right;
            QuickOutputDeviceComboBox.Width = width >= 1420 ? 248 : 196;
            BackendComboBox.Width = width >= 1420 ? 168 : 152;
            BrandSubtitleTextBlock.Visibility = width >= 1360 ? Visibility.Visible : Visibility.Collapsed;
            BrandLogoImage.Width = 38;
            BrandLogoImage.Height = 38;
            BrandTitleTextBlock.FontSize = 21;
            ExclusiveLabelTextBlock.Visibility = Visibility.Visible;
            OutputBarDivider.Visibility = Visibility.Visible;
            OpenFileLabelTextBlock.Visibility = Visibility.Visible;
            ZenithAiLabelTextBlock.Visibility = Visibility.Visible;
            if (RemoteLabelTextBlock is not null)
            {
                RemoteLabelTextBlock.Visibility = Visibility.Visible;
            }
        }

        // 3. Adaptive Workspace Columns (3-col Desktop | 2-col Split-Screen | 1-col Compact)
        double estimatedCenterWidth;
        if (isWide)
        {
            var leftWidth = width >= 1360 ? 284.0 : 244.0;
            var rightWidth = width >= 1360 ? 332.0 : 292.0;
            LeftSidebarColumn.Width = new GridLength(leftWidth);
            CenterStageColumn.Width = new GridLength(1, GridUnitType.Star);
            RightInspectorColumn.Width = new GridLength(rightWidth);
            WorkspaceGrid.ColumnSpacing = width >= 1360 ? 14 : 10;

            LeftSidebarGrid.Visibility = Visibility.Visible;
            MainListeningPanelBorder.Visibility = Visibility.Visible;
            RightInspectorGrid.Visibility = Visibility.Visible;

            ToggleLibraryPaneButton.Visibility = Visibility.Collapsed;
            ToggleInspectorPaneButton.Visibility = Visibility.Collapsed;

            estimatedCenterWidth = Math.Max(320, width - leftWidth - rightWidth - 60);
        }
        else if (isHalfScreen)
        {
            ToggleLibraryPaneButton.Visibility = Visibility.Collapsed;
            ToggleInspectorPaneButton.Visibility = Visibility.Visible;
            ToggleInspectorPaneLabel.Visibility = Visibility.Visible;
            UpdateHeaderPaneToggleVisual(ToggleInspectorPaneButton, isActive: _showInspectorInNarrow);

            if (_showInspectorInNarrow)
            {
                const double rightWidth = 310.0;
                LeftSidebarColumn.Width = new GridLength(0);
                CenterStageColumn.Width = new GridLength(1, GridUnitType.Star);
                RightInspectorColumn.Width = new GridLength(rightWidth);
                LeftSidebarGrid.Visibility = Visibility.Collapsed;
                MainListeningPanelBorder.Visibility = Visibility.Visible;
                RightInspectorGrid.Visibility = Visibility.Visible;
                WorkspaceGrid.ColumnSpacing = 10;
                estimatedCenterWidth = Math.Max(320, width - rightWidth - 40);
            }
            else
            {
                const double leftWidth = 248.0;
                LeftSidebarColumn.Width = new GridLength(leftWidth);
                CenterStageColumn.Width = new GridLength(1, GridUnitType.Star);
                RightInspectorColumn.Width = new GridLength(0);
                LeftSidebarGrid.Visibility = Visibility.Visible;
                MainListeningPanelBorder.Visibility = Visibility.Visible;
                RightInspectorGrid.Visibility = Visibility.Collapsed;
                WorkspaceGrid.ColumnSpacing = 10;
                estimatedCenterWidth = Math.Max(320, width - leftWidth - 40);
            }
        }
        else
        {
            ToggleLibraryPaneButton.Visibility = Visibility.Visible;
            ToggleInspectorPaneButton.Visibility = Visibility.Visible;
            var showPaneLabels = width >= 560;
            ToggleLibraryPaneLabel.Visibility = showPaneLabels ? Visibility.Visible : Visibility.Collapsed;
            ToggleInspectorPaneLabel.Visibility = showPaneLabels ? Visibility.Visible : Visibility.Collapsed;
            UpdateHeaderPaneToggleVisual(ToggleLibraryPaneButton, isActive: _compactPaneMode == 1);
            UpdateHeaderPaneToggleVisual(ToggleInspectorPaneButton, isActive: _compactPaneMode == 2);

            WorkspaceGrid.ColumnSpacing = 0;
            LeftSidebarGrid.Visibility = _compactPaneMode == 1 ? Visibility.Visible : Visibility.Collapsed;
            MainListeningPanelBorder.Visibility = _compactPaneMode == 0 ? Visibility.Visible : Visibility.Collapsed;
            RightInspectorGrid.Visibility = _compactPaneMode == 2 ? Visibility.Visible : Visibility.Collapsed;

            LeftSidebarColumn.Width = _compactPaneMode == 1 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            CenterStageColumn.Width = _compactPaneMode == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            RightInspectorColumn.Width = _compactPaneMode == 2 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

            estimatedCenterWidth = Math.Max(300, width - 24);
        }

        // 4. Adaptive Center Listening Stage (Cover Art & VU/Lyrics split)
        TrackTitleTextBlock.FontSize = isCompact || isShort ? 19 : (width < 1260 ? 22 : 25);

        var showCoverCard = estimatedCenterWidth >= 480 && height >= 560;
        var coverWidth = estimatedCenterWidth >= 680 && !isShort ? 244.0 : 188.0;
        if (showCoverCard)
        {
            CoverArtColumn.Width = new GridLength(coverWidth);
            CoverCardBorder.Width = coverWidth;
            CoverArtImageRow.Height = new GridLength(coverWidth - 24);
            CoverCardBorder.Visibility = Visibility.Visible;
            ListeningContentGrid.ColumnSpacing = coverWidth > 200 ? 16 : 12;
        }
        else
        {
            CoverArtColumn.Width = new GridLength(0);
            CoverCardBorder.Visibility = Visibility.Collapsed;
            ListeningContentGrid.ColumnSpacing = 0;
        }

        var tabContentWidth = estimatedCenterWidth - (showCoverCard ? coverWidth + 16 : 0) - 36;
        if (tabContentWidth < 430)
        {
            LyricsPreviewColumn.Width = new GridLength(0);
            LyricsPreviewCardBorder.Visibility = Visibility.Collapsed;
            ListeningTabSplitGrid.ColumnSpacing = 0;
        }
        else
        {
            VuMeterColumn.Width = new GridLength(1.25, GridUnitType.Star);
            LyricsPreviewColumn.Width = new GridLength(1, GridUnitType.Star);
            LyricsPreviewCardBorder.Visibility = Visibility.Visible;
            ListeningTabSplitGrid.ColumnSpacing = 12;
        }

        // 5. Adaptive Bottom Playback Transport Bar (1-row Desktop vs 2-row Split/Compact)
        if (width >= 1040)
        {
            Grid.SetRow(TimelineGrid, 0);
            Grid.SetColumn(TimelineGrid, 1);
            Grid.SetColumnSpan(TimelineGrid, 1);

            Grid.SetRow(TransportControlsPanel, 0);
            Grid.SetColumn(TransportControlsPanel, 0);
            Grid.SetColumnSpan(TransportControlsPanel, 1);
            TransportControlsPanel.HorizontalAlignment = HorizontalAlignment.Left;

            MiniSpectrumGrid.Visibility = width >= 1240 ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetRow(MiniSpectrumGrid, 0);
            Grid.SetColumn(MiniSpectrumGrid, 2);

            VolumeControlPanel.Visibility = Visibility.Visible;
            Grid.SetRow(VolumeControlPanel, 0);
            Grid.SetColumn(VolumeControlPanel, 3);
            VolumeSlider.Width = width >= 1240 ? 106 : 84;

            ActiveRoutePillBorder.Visibility = Visibility.Visible;
            Grid.SetRow(ActiveRoutePillBorder, 0);
            Grid.SetColumn(ActiveRoutePillBorder, 4);
            ActiveRoutePillBorder.MinWidth = width >= 1240 ? 148 : 126;
        }
        else
        {
            // Row 0: Full-width seek timeline
            Grid.SetRow(TimelineGrid, 0);
            Grid.SetColumn(TimelineGrid, 0);
            Grid.SetColumnSpan(TimelineGrid, 5);

            // Row 1: Transport controls + Volume + optional Route
            MiniSpectrumGrid.Visibility = Visibility.Collapsed;
            Grid.SetRow(TransportControlsPanel, 1);
            Grid.SetColumn(TransportControlsPanel, 0);

            if (width < 600)
            {
                Grid.SetColumnSpan(TransportControlsPanel, 5);
                TransportControlsPanel.HorizontalAlignment = HorizontalAlignment.Center;
                VolumeControlPanel.Visibility = Visibility.Collapsed;
                ActiveRoutePillBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                Grid.SetColumnSpan(TransportControlsPanel, 2);
                TransportControlsPanel.HorizontalAlignment = HorizontalAlignment.Left;

                VolumeControlPanel.Visibility = Visibility.Visible;
                Grid.SetRow(VolumeControlPanel, 1);
                Grid.SetColumn(VolumeControlPanel, 3);
                VolumeSlider.Width = 88;

                ActiveRoutePillBorder.Visibility = width >= 820 ? Visibility.Visible : Visibility.Collapsed;
                Grid.SetRow(ActiveRoutePillBorder, 1);
                Grid.SetColumn(ActiveRoutePillBorder, 4);
                ActiveRoutePillBorder.MinWidth = 128;
            }
        }

        // 6. Keep ZenithAI and Remote Floating Panels responsive inside current window bounds
        if (ZenithAiPanelBorder is not null)
        {
            var aiWidth = Math.Clamp(width - 48, 340, 820);
            var aiHeight = Math.Clamp(height - 48, 380, 680);
            ZenithAiPanelBorder.MinWidth = Math.Min(340, aiWidth);
            ZenithAiPanelBorder.MinHeight = Math.Min(380, aiHeight);
            ZenithAiPanelBorder.Width = aiWidth;
            ZenithAiPanelBorder.Height = aiHeight;
            ClampZenithAiPanelPosition();
        }

        if (RemoteControlPanelBorder is not null)
        {
            RemoteControlPanelBorder.Width = Math.Clamp(width - 32, 300, 430);
            RemoteControlPanelBorder.MaxHeight = Math.Max(320, height - 92);
        }
    }

    private static void UpdateHeaderPaneToggleVisual(Button button, bool isActive)
    {
        button.Background = new SolidColorBrush(isActive
            ? Microsoft.UI.ColorHelper.FromArgb(255, 8, 56, 92)
            : Microsoft.UI.ColorHelper.FromArgb(255, 7, 19, 32));
        button.BorderBrush = new SolidColorBrush(isActive
            ? Microsoft.UI.ColorHelper.FromArgb(255, 56, 178, 248)
            : Microsoft.UI.ColorHelper.FromArgb(255, 23, 61, 91));
    }

    private static string? ResolveBrandAssetPath(string fileName)
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", fileName),
            Path.Combine(AppContext.BaseDirectory, "Imagenes", fileName)
        };

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            candidates.Add(Path.Combine(dir.FullName, "Imagenes", fileName));
            candidates.Add(Path.Combine(dir.FullName, "Assets", fileName));
            dir = dir.Parent;
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private void SetWindowIcon()
    {
        try
        {
            var iconPath = ResolveBrandAssetPath("Icono.ico");
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                AppWindow.SetIcon(iconPath);
                AppWindow.SetTitleBarIcon(iconPath);
                AppWindow.SetTaskbarIcon(iconPath);
            }
        }
        catch
        {
            // Ignore icon errors on unsupported Windows builds.
        }
    }

    private void LoadBrandLogos()
    {
        try
        {
            var logoPath = ResolveBrandAssetPath("Logo.png") ?? ResolveBrandAssetPath("Icono.ico");
            if (string.IsNullOrWhiteSpace(logoPath))
            {
                return;
            }

            var uri = new Uri(logoPath);
            if (BrandLogoImage is not null)
            {
                BrandLogoImage.Source = new BitmapImage(uri);
            }

            if (CoverPlaceholderLogoImage is not null)
            {
                CoverPlaceholderLogoImage.Source = new BitmapImage(uri);
            }
        }
        catch
        {
            // Keep XAML fallback if image cannot be decoded.
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

        await StartPlaybackInternalAsync(_currentFilePath, resumePosition: null);
    }

    private async Task StartPlaybackInternalAsync(string requestedFilePath, TimeSpan? resumePosition)
    {
        var playbackFilePath = await ResolvePlayablePathAsync(requestedFilePath);
        if (playbackFilePath is null)
        {
            return;
        }

        var backend = GetSelectedBackend();
        BackendTextBlock.Text = GetBackendShortLabel(backend);
        ApplyAdaptiveBufferForTrack(playbackFilePath);
        OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn ? "WASAPI Exclusivo" : "WASAPI Compartido";
        PlayButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = "Preparando reproducción";

        try
        {
            StopCurrentPlaybackForTrackChange();
            _activePlaybackFilePath = playbackFilePath;

            var extension = Path.GetExtension(playbackFilePath);
            var isDsd = DsdExtensions.Contains(extension);
            var dsdOptions = GetCurrentDsdPlaybackOptions();
            var useExternalNative = backend is AudioBackend.BassWasapi or AudioBackend.MpvWasapi && IsNativeBackendAvailable(backend, playbackFilePath);
            var mpvWithoutExclusiveDac = backend == AudioBackend.MpvWasapi && !IsExclusiveDacSelected();
            var startPosition = resumePosition ?? (_pendingCueEntry is not null && _pendingCueEntry.Start > TimeSpan.Zero ? _pendingCueEntry.Start : null);

            if (backend == AudioBackend.AsioNative)
            {
                PlayWithAsio(playbackFilePath, dsdOptions, startPosition);
            }
            else if (useExternalNative)
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
                if (startPosition is { } seekStart && seekStart > TimeSpan.Zero)
                {
                    _audioEngine.Seek(seekStart);
                }

                _usingWasapiEngine = false;
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

                StatusInfoBar.Severity = InfoBarSeverity.Success;
                StatusInfoBar.Message = ExclusiveModeToggle.IsOn && !mpvWithoutExclusiveDac
                    ? "Reproduciendo en modo exclusivo"
                    : "Reproduciendo en modo compartido";
            }
            else if (CanUseIntegratedWasapi(playbackFilePath, dsdOptions))
            {
                await PlayWithIntegratedWasapiAsync(playbackFilePath, dsdOptions, startPosition);
            }
            else
            {
                if (isDsd && dsdOptions.OutputMode == DsdOutputMode.NativeExternalOnly)
                {
                    throw new InvalidOperationException("Modo 'Solo DSD nativo (BASS / MPV)' activo y faltan las DLL nativas. Cambia el modo DSD a Auto o PCM FIR para reproducir sin DLL externas.");
                }

                ApplyWindowsMaximumQualityFallback(backend, isDsd ? "DSD nativo no disponible" : "Backend nativo no disponible");
                await PlayWithWindowsFallbackAsync(playbackFilePath);
                StatusInfoBar.Severity = InfoBarSeverity.Success;
                StatusInfoBar.Message = "Reproduciendo con Windows fallback a la mayor calidad negociada por el sistema";
            }

            PlayButton.IsEnabled = true;
            PauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            _isPaused = false;
            if (!_usingWasapiEngine && !_usingAsioEngine)
            {
                TransportBadgeTextBlock.Text = "REPRODUCIENDO";
            }
            RefreshZenithAiPlayerStrip();
        }
        catch (Exception ex)
        {
            _usingWasapiEngine = false;
            _usingAsioEngine = false;
            _usingFallbackPlayer = false;
            UpdatePlaybackAvailabilityV2();
            PauseButton.IsEnabled = false;
            StopButton.IsEnabled = false;
            TransportBadgeTextBlock.Text = "ERROR";
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = ex.Message;
        }
    }

    private void PlayWithAsio(
        string playbackFilePath,
        DsdPlaybackOptions dsdOptions,
        TimeSpan? startPosition)
    {
        var selectedDriver = GetSelectedAsioDriver()
            ?? throw new InvalidOperationException("No hay un controlador ASIO seleccionado. Instala el driver ASIO de tu DAC (por ejemplo FiiO ASIO Driver) y pulsa Actualizar.");

        _asioEngine.Volume = (float)Math.Clamp(VolumeSlider.Value / 100.0, 0.0, 1.0);
        var windowHandle = WindowNative.GetWindowHandle(this);

        var report = _asioEngine.Play(
            playbackFilePath,
            selectedDriver.Name,
            windowHandle,
            _toneSettings,
            dsdOptions,
            startPosition);

        _usingAsioEngine = true;
        _usingWasapiEngine = false;
        _usingFallbackPlayer = false;
        _vuUsesLiveLevel = true;
        _vuUsesAnalyzer = false;
        CleanupTemporaryFallbackFile();

        var duration = _asioEngine.Duration;
        var position = _asioEngine.Position;
        _isUpdatingPlaybackProgress = true;
        PlaybackProgressSlider.Maximum = Math.Max(duration.TotalSeconds, 1);
        PlaybackProgressSlider.Value = Math.Clamp(position.TotalSeconds, 0, PlaybackProgressSlider.Maximum);
        _isUpdatingPlaybackProgress = false;
        CurrentTimeTextBlock.Text = FormatTime(position);
        DurationTextBlock.Text = FormatTime(duration);
        _currentLyricIndex = -1;
        UpdateSyncedLyrics(position);

        SampleRateTextBlock.Text = report.IsNativeDsd
            ? $"{report.SampleRate / 1_000_000.0:0.##} MHz"
            : $"{report.SampleRate / 1000.0:0.#} kHz";
        BitDepthTextBlock.Text = report.IsNativeDsd ? "1 bit" : $"{report.BitDepth} bit";
        ChannelsTextBlock.Text = report.Channels.ToString(CultureInfo.InvariantCulture);
        CodecTextBlock.Text = report.SourceCodec;
        ModeTextBlock.Text = report.IsNativeDsd
            ? "ASIO DSD Nativo"
            : report.IsBitPerfect ? "ASIO Bit-Perfect" : "ASIO PCM";
        OutputPathTextBlock.Text = report.DriverName;
        TransportBadgeTextBlock.Text = report.IsNativeDsd ? "DSD NATIVO" : "ASIO PCM";
        VolumeTextBlock.Text = report.IsNativeDsd
            ? "Volumen 100% (DSD)"
            : $"Volumen {(int)VolumeSlider.Value}%";

        _playbackTimer.Start();
        StartVuMeter();
        UpdateSignalChain(report.DecodeLabel);
        WritePlaybackAuditLog(playbackFilePath, report.Summary);

        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.Message = report.Summary;
    }

    private void AsioEngine_LevelChanged(object? sender, double level)
    {
        _vuTargetLevel = MapLiveVuLevel(level);
    }

    private void AsioEngine_TrackEnded(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_usingAsioEngine)
            {
                return;
            }

            _playbackTimer.Stop();
            _vuTimer.Stop();
            _asioEngine.Stop();
            _usingAsioEngine = false;
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

    private async Task PlayWithIntegratedWasapiAsync(
        string playbackFilePath,
        DsdPlaybackOptions dsdOptions,
        TimeSpan? startPosition)
    {
        var selectedOutputDevice = GetSelectedOutputDevice();
        var hasSpecificDevice = selectedOutputDevice is not null && !selectedOutputDevice.IsSystemDefault;
        var deviceId = hasSpecificDevice ? selectedOutputDevice!.Id : null;
        var deviceName = hasSpecificDevice ? selectedOutputDevice!.Name : null;
        var requestExclusive = ExclusiveModeToggle.IsOn;
        var bufferMs = (int)BufferSlider.Value;

        _wasapiEngine.Volume = (float)Math.Clamp(VolumeSlider.Value / 100.0, 0.0, 1.0);

        WasapiPlaybackReport report;
        try
        {
            report = await Task.Run(() => _wasapiEngine.Play(
                playbackFilePath,
                deviceId,
                deviceName,
                requestExclusive,
                bufferMs,
                _toneSettings,
                dsdOptions,
                startPosition));
        }
        catch when (!DsdExtensions.Contains(Path.GetExtension(playbackFilePath)) &&
                    WindowsFallbackExtensions.Contains(Path.GetExtension(playbackFilePath)))
        {
            await PlayWithWindowsFallbackAsync(playbackFilePath);
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = "WASAPI integrado no pudo abrir este códec; reproduciendo con Windows Media Foundation.";
            return;
        }

        _usingWasapiEngine = true;
        _usingFallbackPlayer = false;
        _vuUsesLiveLevel = true;
        _vuUsesAnalyzer = false;
        CleanupTemporaryFallbackFile();

        var duration = _wasapiEngine.Duration;
        var position = _wasapiEngine.Position;
        _isUpdatingPlaybackProgress = true;
        PlaybackProgressSlider.Maximum = Math.Max(duration.TotalSeconds, 1);
        PlaybackProgressSlider.Value = Math.Clamp(position.TotalSeconds, 0, PlaybackProgressSlider.Maximum);
        _isUpdatingPlaybackProgress = false;
        CurrentTimeTextBlock.Text = FormatTime(position);
        DurationTextBlock.Text = FormatTime(duration);
        _currentLyricIndex = -1;
        UpdateSyncedLyrics(position);

        SampleRateTextBlock.Text = $"{report.OutputSampleRate / 1000.0:0.#} kHz";
        BitDepthTextBlock.Text = report.IsDoP ? "1 bit (DoP 24)" : $"{report.OutputBitDepth} bit";
        ChannelsTextBlock.Text = report.OutputChannels.ToString(CultureInfo.InvariantCulture);
        CodecTextBlock.Text = report.SourceCodec;
        ModeTextBlock.Text = report.IsDoP
            ? "DoP Bit-Perfect"
            : report.IsBitPerfect
                ? "Bit-Perfect"
                : report.IsExclusive
                    ? (report.IsDsdSource ? "DSD->PCM Excl." : "WASAPI Excl.")
                    : (report.IsDsdSource ? "DSD->PCM Comp." : "WASAPI Comp.");
        OutputPathTextBlock.Text = report.IsExclusive
            ? (report.IsEventDriven ? "WASAPI Excl. (Event)" : "WASAPI Excl. (Push)")
            : "WASAPI Compartido";
        TransportBadgeTextBlock.Text = report.IsDoP
            ? "DoP DSD"
            : report.IsDsdSource
                ? "DSD FIR"
                : "REPRODUCIENDO";

        _playbackTimer.Start();
        StartVuMeter();
        UpdateSignalChain(report.DecodeLabel);
        WritePlaybackAuditLog(playbackFilePath, report.Summary);

        StatusInfoBar.Severity = report.IsExclusive || !requestExclusive
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Warning;
        StatusInfoBar.Message = report.Summary;
    }

    private void WasapiEngine_LevelChanged(object? sender, double level)
    {
        _vuTargetLevel = MapLiveVuLevel(level);
    }

    private void WasapiEngine_TrackEnded(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_usingWasapiEngine)
            {
                return;
            }

            _playbackTimer.Stop();
            _vuTimer.Stop();
            _usingWasapiEngine = false;
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

    private async Task HotSwitchActivePlaybackAsync()
    {
        if (_isLoadingAudioSettings || string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        var isActivelyPlaying = _usingAsioEngine
            ? _asioEngine.IsPlaying
            : _usingWasapiEngine
                ? _wasapiEngine.IsPlaying
                : _usingFallbackPlayer
                    ? _fallbackMediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing
                    : StopButton.IsEnabled && !_isPaused;

        if (!isActivelyPlaying)
        {
            return;
        }

        var currentPos = _usingAsioEngine
            ? _asioEngine.Position
            : _usingWasapiEngine
                ? _wasapiEngine.Position
                : _usingFallbackPlayer
                    ? _fallbackMediaPlayer.PlaybackSession.Position
                    : TimeSpan.Zero;

        await StartPlaybackInternalAsync(_currentFilePath, currentPos > TimeSpan.Zero ? currentPos : null);
    }

    private static bool CanUseIntegratedWasapi(string filePath, DsdPlaybackOptions dsdOptions)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".dsf" or ".dff")
        {
            return dsdOptions.OutputMode != DsdOutputMode.NativeExternalOnly;
        }

        return WindowsFallbackExtensions.Contains(ext) || ext is ".alac";
    }

    private DsdPlaybackOptions GetCurrentDsdPlaybackOptions()
    {
        var mode = (DsdModeComboBox?.SelectedIndex ?? 0) switch
        {
            0 => DsdOutputMode.PcmFir,
            1 => DsdOutputMode.DoPExclusive,
            2 => DsdOutputMode.NativeExternalOnly,
            _ => DsdOutputMode.PcmFir
        };

        var targetRate = 0;
        if (DsdPcmRateComboBox?.SelectedItem is ComboBoxItem { Tag: string rateTag } &&
            int.TryParse(rateTag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRate))
        {
            targetRate = parsedRate;
        }

        var gainDb = 6.0;
        if (DsdGainComboBox?.SelectedItem is ComboBoxItem { Tag: string gainTag } &&
            double.TryParse(gainTag, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedGain))
        {
            gainDb = parsedGain;
        }

        return new DsdPlaybackOptions(mode, targetRate, gainDb);
    }

    private static string GetBackendShortLabel(AudioBackend backend)
    {
        return backend switch
        {
            AudioBackend.ZenithWasapi => "WASAPI",
            AudioBackend.AsioNative => "ASIO",
            AudioBackend.MpvWasapi => "MPV",
            _ => "BASS"
        };
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_usingAsioEngine)
        {
            _asioEngine.Stop();
            _usingAsioEngine = false;
            _playbackTimer.Stop();
            _vuTimer.Stop();
            PlaybackProgressSlider.Value = 0;
            CurrentTimeTextBlock.Text = "0:00";
            DurationTextBlock.Text = "0:00";
        }
        else if (_usingWasapiEngine)
        {
            _wasapiEngine.Stop();
            _usingWasapiEngine = false;
            _playbackTimer.Stop();
            _vuTimer.Stop();
            PlaybackProgressSlider.Value = 0;
            CurrentTimeTextBlock.Text = "0:00";
            DurationTextBlock.Text = "0:00";
        }
        else if (_usingFallbackPlayer)
        {
            _fallbackMediaPlayer.Pause();
            _fallbackMediaPlayer.Source = null;
            _usingFallbackPlayer = false;
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
            if (_usingAsioEngine)
            {
                _asioEngine.Pause();
            }
            else if (_usingWasapiEngine)
            {
                _wasapiEngine.Pause();
            }
            else if (_usingFallbackPlayer)
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
            RefreshZenithAiPlayerStrip();
            return;
        }

        if (_usingAsioEngine)
        {
            _asioEngine.Resume();
        }
        else if (_usingWasapiEngine)
        {
            _wasapiEngine.Resume();
        }
        else if (_usingFallbackPlayer)
        {
            _fallbackMediaPlayer.Play();
        }
        else
        {
            _audioEngine.Resume();
        }

        _isPaused = false;
        TransportBadgeTextBlock.Text = _asioEngine.IsNativeDsdActive
            ? "DSD NATIVO"
            : _wasapiEngine.IsDoPActive ? "DoP DSD" : "REPRODUCIENDO";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.Message = "Reproduciendo";
        _playbackTimer.Start();
        StartVuMeter();
        RefreshZenithAiPlayerStrip();
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
        _wasapiEngine.Volume = (float)volume;
        _asioEngine.Volume = (float)volume;
        VolumeTextBlock.Text = _asioEngine.IsNativeDsdActive
            ? "Volumen 100% (DSD)"
            : _wasapiEngine.IsDoPActive
                ? "Volumen 100% (DoP)"
                : $"Volumen {(int)e.NewValue}%";
    }

    private async void BackendComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackendTextBlock is null)
        {
            return;
        }

        var backend = GetSelectedBackend();
        BackendTextBlock.Text = GetBackendShortLabel(backend);
        SaveAudioSettings();
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
        await HotSwitchActivePlaybackAsync();
    }

    private async void OutputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingOutputSelectors || StatusInfoBar is null || OutputPathTextBlock is null)
        {
            return;
        }

        SyncOutputSelectors(fromQuickSelector: false);
        EnforceExclusiveDriverGuard(showAlert: false);
        SaveAudioSettings();
        ApplyOutputDeviceSettings();
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
        await HotSwitchActivePlaybackAsync();
    }

    private async void QuickOutputDeviceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingOutputSelectors || StatusInfoBar is null || OutputPathTextBlock is null)
        {
            return;
        }

        SyncOutputSelectors(fromQuickSelector: true);
        EnforceExclusiveDriverGuard(showAlert: false);
        SaveAudioSettings();
        ApplyOutputDeviceSettings();
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
        await HotSwitchActivePlaybackAsync();
    }

    private void SyncOutputSelectors(bool fromQuickSelector)
    {
        if (OutputDeviceComboBox is null || QuickOutputDeviceComboBox is null)
        {
            return;
        }

        _isSyncingOutputSelectors = true;
        try
        {
            if (fromQuickSelector)
            {
                OutputDeviceComboBox.SelectedIndex = QuickOutputDeviceComboBox.SelectedIndex;
            }
            else
            {
                QuickOutputDeviceComboBox.SelectedIndex = OutputDeviceComboBox.SelectedIndex;
            }
        }
        finally
        {
            _isSyncingOutputSelectors = false;
        }
    }

    private void InitializeOutputDeviceList()
    {
        _isSyncingOutputSelectors = true;
        try
        {
            _outputDevices.Clear();
            _outputDevices.Add(OutputDeviceOption.SystemDefault);
            OutputDeviceComboBox.ItemsSource = null;
            OutputDeviceComboBox.ItemsSource = _outputDevices;
            OutputDeviceComboBox.SelectedIndex = 0;

            if (QuickOutputDeviceComboBox is not null)
            {
                QuickOutputDeviceComboBox.ItemsSource = null;
                QuickOutputDeviceComboBox.ItemsSource = _outputDevices;
                QuickOutputDeviceComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _isSyncingOutputSelectors = false;
        }
    }

    private async Task RefreshOutputDevicesAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.AudioRender);

            DispatcherQueue.TryEnqueue(() =>
            {
                var selectedId = GetSelectedOutputDevice()?.Id ?? _savedAudioSettings?.DeviceId;
                _isSyncingOutputSelectors = true;
                try
                {
                    _outputDevices.Clear();
                    _outputDevices.Add(OutputDeviceOption.SystemDefault);

                    foreach (var device in devices.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase))
                    {
                        _outputDevices.Add(OutputDeviceOption.FromDevice(device));
                    }

                    OutputDeviceComboBox.ItemsSource = null;
                    OutputDeviceComboBox.ItemsSource = _outputDevices;

                    if (QuickOutputDeviceComboBox is not null)
                    {
                        QuickOutputDeviceComboBox.ItemsSource = null;
                        QuickOutputDeviceComboBox.ItemsSource = _outputDevices;
                    }

                    var selectedIndex = _outputDevices.FindIndex(device => !string.IsNullOrWhiteSpace(selectedId) && device.Id == selectedId);
                    if (selectedIndex < 0)
                    {
                        selectedIndex = _outputDevices.FindIndex(device => device.IsKnownZishanZ2);
                    }

                    var resolvedIndex = selectedIndex >= 0 ? selectedIndex : 0;
                    OutputDeviceComboBox.SelectedIndex = resolvedIndex;
                    if (QuickOutputDeviceComboBox is not null)
                    {
                        QuickOutputDeviceComboBox.SelectedIndex = resolvedIndex;
                    }
                }
                finally
                {
                    _isSyncingOutputSelectors = false;
                }

                StatusInfoBar.Severity = InfoBarSeverity.Informational;
                StatusInfoBar.Message = devices.Count > 0
                    ? $"Dispositivos de salida detectados: {devices.Count}. Puedes cambiar la salida arriba o en Ajustes del sistema."
                    : "Windows no reportó dispositivos de salida. Se usará el dispositivo predeterminado del sistema.";

                EnforceExclusiveDriverGuard(showAlert: false);
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
        StatusInfoBar.Message = "Buscando dispositivos de salida y controladores ASIO en Windows...";
        RefreshAsioDrivers();
        await RefreshOutputDevicesAsync();
    }

    private void RefreshAsioDrivers()
    {
        if (AsioDriverComboBox is null)
        {
            return;
        }

        _isLoadingAsioDrivers = true;
        try
        {
            var previousName = GetSelectedAsioDriver()?.Name ?? _savedAudioSettings?.AsioDriverName;
            _asioDrivers.Clear();
            _asioDrivers.AddRange(AsioPlaybackEngine.GetAvailableDrivers());

            AsioDriverComboBox.ItemsSource = null;
            AsioDriverComboBox.ItemsSource = _asioDrivers;

            if (_asioDrivers.Count > 0)
            {
                var idx = !string.IsNullOrWhiteSpace(previousName)
                    ? _asioDrivers.FindIndex(d => d.Name.Equals(previousName, StringComparison.OrdinalIgnoreCase))
                    : -1;
                AsioDriverComboBox.SelectedIndex = idx >= 0 ? idx : 0;
            }

            if (OpenAsioPanelButton is not null)
            {
                OpenAsioPanelButton.IsEnabled = _asioDrivers.Count > 0;
            }
        }
        finally
        {
            _isLoadingAsioDrivers = false;
        }

        RefreshBackendLabels();
    }

    private AsioDriverInfo? GetSelectedAsioDriver()
    {
        return AsioDriverComboBox?.SelectedItem as AsioDriverInfo ?? _asioDrivers.FirstOrDefault();
    }

    private async void AsioDriverComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingAsioDrivers || StatusInfoBar is null)
        {
            return;
        }

        SaveAudioSettings();
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
        if (GetSelectedBackend() == AudioBackend.AsioNative)
        {
            await HotSwitchActivePlaybackAsync();
        }
    }

    private void OpenAsioControlPanelButton_Click(object sender, RoutedEventArgs e)
    {
        var driver = GetSelectedAsioDriver();
        if (driver is null)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = "No se detectó ningún controlador ASIO instalado en Windows.";
            return;
        }

        try
        {
            AsioPlaybackEngine.OpenControlPanel(driver.Name, WindowNative.GetWindowHandle(this));
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = $"Panel de control ASIO abierto: {driver.Name}";
        }
        catch (Exception ex)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = $"No se pudo abrir el panel ASIO: {ex.Message}";
        }
    }

    private void ZenithAiButton_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteControlOverlayGrid is not null)
        {
            RemoteControlOverlayGrid.Visibility = Visibility.Collapsed;
        }

        ConfigureZenithAiPanelLayout();
        RefreshZenithAiSettingsUi();
        RefreshZenithAiPlayerStrip();
        SelectZenithAiTab(_zenithAiClient.IsConfigured ? 0 : 2);
        RenderZenithAiTranscript(ZenithAiTranscriptPanel, ZenithAiTranscriptScrollViewer, GetZenithAiTranscriptWidth());
        ZenithAiOverlayGrid.Visibility = Visibility.Visible;
        if (_zenithAiClient.IsConfigured)
        {
            ZenithAiQuestionTextBox.Focus(FocusState.Programmatic);
        }
    }

    private void ConfigureZenithAiPanelLayout()
    {
        var windowWidth = _lastLayoutWidth > 0 ? _lastLayoutWidth : Math.Max(900, AppWindow.Size.Width);
        var windowHeight = _lastLayoutHeight > 0 ? _lastLayoutHeight : Math.Max(700, AppWindow.Size.Height);
        var aiWidth = Math.Clamp(windowWidth - 48, 340, 820);
        var aiHeight = Math.Clamp(windowHeight - 48, 360, 680);
        ZenithAiPanelBorder.MinWidth = Math.Min(340, aiWidth);
        ZenithAiPanelBorder.MinHeight = Math.Min(360, aiHeight);
        ZenithAiPanelBorder.Width = aiWidth;
        ZenithAiPanelBorder.Height = aiHeight;
        ClampZenithAiPanelPosition();
    }

    private void SelectZenithAiTab(int tabIndex)
    {
        if (ZenithAiChatViewGrid is null || ZenithAiCommandsPanel is null || ZenithAiApiSettingsPanel is null)
        {
            return;
        }

        ZenithAiChatViewGrid.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        ZenithAiCommandsPanel.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        ZenithAiApiSettingsPanel.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

        UpdateRightTabButtonStyle(ZenithAiTabChatButton, isActive: tabIndex == 0);
        UpdateRightTabButtonStyle(ZenithAiCommandsButton, isActive: tabIndex == 1);
        UpdateRightTabButtonStyle(ZenithAiConfigButton, isActive: tabIndex == 2);

        if (tabIndex == 0)
        {
            ScrollZenithAiTranscriptToEnd(ZenithAiTranscriptScrollViewer);
        }
        else if (tabIndex == 2)
        {
            ZenithAiStatusTextBlock.Text = "Elige cualquier proveedor OpenAI-compatible, extrae sus modelos disponibles y guarda.";
        }
    }

    private void RefreshZenithAiSettingsUi()
    {
        _zenithAiClient.ReloadSettings();
        ZenithAiProviderTextBox.Text = _zenithAiClient.Settings.Provider;
        ZenithAiEndpointTextBox.Text = _zenithAiClient.Settings.Endpoint;
        ZenithAiModelTextBox.Text = _zenithAiClient.Settings.Model;
        ZenithAiApiKeyBox.Password = _zenithAiClient.Settings.ApiKey;

        SyncZenithAiProviderPresetSelection(_zenithAiClient.Settings.Provider, _zenithAiClient.Settings.Endpoint);
        UpdateZenithAiModelBadge();

        ZenithAiStatusTextBlock.Text = _zenithAiClient.IsConfigured
            ? $"Conectado a {_zenithAiClient.Settings.Provider} ({_zenithAiClient.Settings.Model}) · Integración directa con el reproductor activa."
            : "Configura un proveedor en 'Config API' (NVIDIA NIM, OpenAI, OpenRouter, Groq, Ollama local, etc.).";
    }

    private void UpdateZenithAiModelBadge()
    {
        if (ZenithAiActiveModelBadgeTextBlock is null)
        {
            return;
        }

        var provider = string.IsNullOrWhiteSpace(_zenithAiClient.Settings.Provider) ? "API" : _zenithAiClient.Settings.Provider;
        var model = string.IsNullOrWhiteSpace(_zenithAiClient.Settings.Model) ? "sin modelo" : _zenithAiClient.Settings.Model;
        var slash = model.LastIndexOf('/');
        var shortModel = slash >= 0 && slash < model.Length - 1 ? model[(slash + 1)..] : model;
        ZenithAiActiveModelBadgeTextBlock.Text = $"{provider} · {shortModel}";
    }

    private void SyncZenithAiProviderPresetSelection(string provider, string endpoint)
    {
        if (ZenithAiProviderPresetComboBox is null)
        {
            return;
        }

        _isSyncingZenithAiProviderPreset = true;
        try
        {
            var presets = ZenithAiClient.ProviderPresets;
            var matchIndex = -1;
            for (var i = 0; i < presets.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(presets[i].Endpoint) &&
                    (presets[i].Endpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase) ||
                     presets[i].Name.Equals(provider, StringComparison.OrdinalIgnoreCase)))
                {
                    matchIndex = i;
                    break;
                }
            }

            ZenithAiProviderPresetComboBox.SelectedIndex = matchIndex >= 0 ? matchIndex : presets.Count - 1;
        }
        finally
        {
            _isSyncingZenithAiProviderPreset = false;
        }
    }

    private void RefreshZenithAiPlayerStrip()
    {
        if (ZenithAiNowPlayingTitleTextBlock is null || ZenithAiNowPlayingMetaTextBlock is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            ZenithAiNowPlayingTitleTextBlock.Text = "Sin pista cargada";
            ZenithAiNowPlayingMetaTextBlock.Text = $"{_libraryTracks.Count} pistas en biblioteca · Pide a ZenithAI reproducir, cambiar a ASIO o ajustar el EQ";
        }
        else
        {
            ZenithAiNowPlayingTitleTextBlock.Text = TrackTitleTextBlock?.Text ?? Path.GetFileName(_currentFilePath);
            ZenithAiNowPlayingMetaTextBlock.Text = $"{CodecTextBlock?.Text} · {SampleRateTextBlock?.Text} · {BackendTextBlock?.Text} · {OutputPathTextBlock?.Text}";
        }

        if (ZenithAiPlayPauseIcon is not null)
        {
            var isActivelyPlaying = _usingAsioEngine
                ? _asioEngine.IsPlaying
                : _usingWasapiEngine
                    ? _wasapiEngine.IsPlaying
                    : StopButton?.IsEnabled == true && !_isPaused;
            ZenithAiPlayPauseIcon.Glyph = isActivelyPlaying ? "\uE769" : "\uE768";
        }
    }

    private void ZenithAiPlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (StopButton.IsEnabled)
        {
            PauseButton_Click(this, e);
        }
        else if (!string.IsNullOrWhiteSpace(_currentFilePath))
        {
            PlayButton_Click(this, e);
        }
        else if (_libraryTracks.Count > 0)
        {
            _ = LoadLibraryTrackAsync(_libraryTracks[0], play: true);
        }

        RefreshZenithAiPlayerStrip();
    }

    private double GetZenithAiTranscriptWidth()
    {
        return Math.Max(300, ZenithAiPanelBorder.Width - 72);
    }

    private void ZenithAiCloseButton_Click(object sender, RoutedEventArgs e)
    {
        _zenithAiRequestCancellation?.Cancel();
        _zenithAiRequestCancellation?.Dispose();
        _zenithAiRequestCancellation = null;
        ZenithAiOverlayGrid.Visibility = Visibility.Collapsed;
    }

    private void ZenithAiClearButton_Click(object sender, RoutedEventArgs e)
    {
        _zenithAiMessages.Clear();
        RenderZenithAiTranscript(ZenithAiTranscriptPanel, ZenithAiTranscriptScrollViewer, GetZenithAiTranscriptWidth());
        ZenithAiStatusTextBlock.Text = "Historial limpio. Puedes chatear o controlar Zenith Audio desde aquí.";
    }

    private void ZenithAiTabChatButton_Click(object sender, RoutedEventArgs e)
    {
        SelectZenithAiTab(0);
    }

    private void ZenithAiConfigButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshZenithAiSettingsUi();
        SelectZenithAiTab(2);
    }

    private void ZenithAiCommandsButton_Click(object sender, RoutedEventArgs e)
    {
        SelectZenithAiTab(1);
        ZenithAiStatusTextBlock.Text = "Haz clic en cualquier acción para aplicarla directamente en Zenith Audio.";
    }

    private async void ZenithAiQuickAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string command })
        {
            return;
        }

        SelectZenithAiTab(0);
        ZenithAiQuestionTextBox.Text = command;
        await SendZenithAiQuestionAsync();
    }

    private void ZenithAiProviderPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingZenithAiProviderPreset || ZenithAiProviderPresetComboBox?.SelectedItem is not ZenithAiProviderPreset preset)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(preset.Endpoint))
        {
            ZenithAiProviderTextBox.Text = preset.Name;
            ZenithAiEndpointTextBox.Text = preset.Endpoint;
            if (!string.IsNullOrWhiteSpace(preset.SuggestedModel))
            {
                ZenithAiModelTextBox.Text = preset.SuggestedModel;
            }
        }

        _zenithAiFetchedModels.Clear();
        ZenithAiModelsListView.ItemsSource = null;
        ZenithAiModelFilterTextBox.Text = string.Empty;
        ZenithAiModelsCountTextBlock.Text = $"{preset.Hint}. Pulsa 'Extraer modelos' para cargar su catálogo.";
    }

    private async void ZenithAiFetchModelsButton_Click(object sender, RoutedEventArgs e)
    {
        var endpoint = ZenithAiEndpointTextBox.Text.Trim();
        var apiKey = ZenithAiApiKeyBox.Password.Trim();

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            ZenithAiStatusTextBlock.Text = "Ingresa el endpoint del proveedor antes de extraer modelos.";
            return;
        }

        ZenithAiFetchModelsButton.IsEnabled = false;
        ZenithAiTestConnectionButton.IsEnabled = false;
        ZenithAiModelsCountTextBlock.Text = "Consultando catálogo de modelos del proveedor...";
        ZenithAiStatusTextBlock.Text = $"Extrayendo modelos desde {FormatEndpointForDiagnostics(endpoint)}...";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var models = await _zenithAiClient.FetchModelsAsync(endpoint, apiKey, cts.Token);
            _zenithAiFetchedModels.Clear();
            _zenithAiFetchedModels.AddRange(models);
            ApplyZenithAiModelFilter();
            ZenithAiModelsCountTextBlock.Text = $"{models.Count} modelos extraídos. Haz clic en uno para seleccionarlo.";
            ZenithAiStatusTextBlock.Text = $"Catálogo cargado ({models.Count} modelos disponibles).";
        }
        catch (Exception ex)
        {
            ZenithAiModelsCountTextBlock.Text = "No se pudo extraer la lista automáticamente (puedes escribir el ID del modelo manualmente).";
            ZenithAiStatusTextBlock.Text = $"Error al extraer modelos: {ex.Message}";
        }
        finally
        {
            ZenithAiFetchModelsButton.IsEnabled = true;
            ZenithAiTestConnectionButton.IsEnabled = true;
        }
    }

    private async void ZenithAiTestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        var provider = string.IsNullOrWhiteSpace(ZenithAiProviderTextBox.Text) ? "API compatible" : ZenithAiProviderTextBox.Text.Trim();
        var endpoint = ZenithAiEndpointTextBox.Text.Trim();
        var model = ZenithAiModelTextBox.Text.Trim();
        var apiKey = ZenithAiApiKeyBox.Password.Trim();

        ZenithAiTestConnectionButton.IsEnabled = false;
        ZenithAiFetchModelsButton.IsEnabled = false;
        ZenithAiStatusTextBlock.Text = $"Probando conexión con {provider} ({model})...";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            var result = await _zenithAiClient.TestConnectionAsync(provider, endpoint, model, apiKey, cts.Token);
            ZenithAiStatusTextBlock.Text = result;
        }
        catch (Exception ex)
        {
            ZenithAiStatusTextBlock.Text = $"Prueba fallida: {ex.Message}";
        }
        finally
        {
            ZenithAiTestConnectionButton.IsEnabled = true;
            ZenithAiFetchModelsButton.IsEnabled = true;
        }
    }

    private void ZenithAiModelFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyZenithAiModelFilter();
    }

    private void ApplyZenithAiModelFilter()
    {
        if (ZenithAiModelsListView is null)
        {
            return;
        }

        var query = ZenithAiModelFilterTextBox?.Text?.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _zenithAiFetchedModels
            : _zenithAiFetchedModels
                .Where(m =>
                    m.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    m.Owner.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    m.ContextBadge.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        ZenithAiModelsListView.ItemsSource = null;
        ZenithAiModelsListView.ItemsSource = filtered;
        if (_zenithAiFetchedModels.Count > 0)
        {
            ZenithAiModelsCountTextBlock.Text = string.IsNullOrWhiteSpace(query)
                ? $"{filtered.Count} modelos extraídos. Haz clic en uno para seleccionarlo."
                : $"{filtered.Count} de {_zenithAiFetchedModels.Count} modelos coinciden con '{query}'.";
        }
    }

    private void ZenithAiModelsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ZenithAiModelInfo model)
        {
            ZenithAiModelTextBox.Text = model.Id;
            ZenithAiStatusTextBlock.Text = $"Modelo seleccionado: {model.Id}. Pulsa 'Guardar y abrir Chat' para activarlo.";
        }
    }

    private void ZenithAiSaveApiButton_Click(object sender, RoutedEventArgs e)
    {
        var endpoint = ZenithAiClient.NormalizeChatEndpoint(ZenithAiEndpointTextBox.Text);
        var settings = new ZenithAiSettings(
            string.IsNullOrWhiteSpace(ZenithAiProviderTextBox.Text) ? "API compatible" : ZenithAiProviderTextBox.Text.Trim(),
            endpoint,
            ZenithAiModelTextBox.Text.Trim(),
            ZenithAiApiKeyBox.Password.Trim());
        ZenithAiSettings.Save(settings);
        _zenithAiClient.ReloadSettings();
        ZenithAiEndpointTextBox.Text = endpoint;
        UpdateZenithAiModelBadge();
        ZenithAiStatusTextBlock.Text = _zenithAiClient.IsConfigured
            ? $"Configuración guardada: {_zenithAiClient.Settings.Provider} · {_zenithAiClient.Settings.Model}"
            : "Configuración guardada, pero falta completar API key o modelo.";

        if (_zenithAiClient.IsConfigured)
        {
            SelectZenithAiTab(0);
        }
    }

    private void ZenithAiResetApiButton_Click(object sender, RoutedEventArgs e)
    {
        ZenithAiProviderTextBox.Text = ZenithAiSettings.DefaultProvider;
        ZenithAiEndpointTextBox.Text = ZenithAiSettings.DefaultEndpoint;
        ZenithAiModelTextBox.Text = ZenithAiSettings.DefaultModel;
        SyncZenithAiProviderPresetSelection(ZenithAiSettings.DefaultProvider, ZenithAiSettings.DefaultEndpoint);
        var settings = new ZenithAiSettings(
            ZenithAiSettings.DefaultProvider,
            ZenithAiSettings.DefaultEndpoint,
            ZenithAiSettings.DefaultModel,
            ZenithAiApiKeyBox.Password.Trim());
        ZenithAiSettings.Save(settings);
        _zenithAiClient.ReloadSettings();
        UpdateZenithAiModelBadge();
        ZenithAiStatusTextBlock.Text = "NVIDIA NIM restaurado. Puedes pulsar 'Extraer modelos' o guardar.";
    }

    private async void ZenithAiSendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendZenithAiQuestionAsync();
    }

    private void ZenithAiCancelButton_Click(object sender, RoutedEventArgs e)
    {
        _zenithAiCancelRequestedByUser = true;
        ZenithAiStatusTextBlock.Text = "Cancelando consulta...";
        _zenithAiRequestCancellation?.Cancel();
    }

    private async void ZenithAiQuestionTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        await SendZenithAiQuestionAsync();
    }

    private async Task SendZenithAiQuestionAsync()
    {
        var question = ZenithAiQuestionTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(question) || !ZenithAiSendButton.IsEnabled)
        {
            return;
        }

        SelectZenithAiTab(0);
        ZenithAiQuestionTextBox.Text = string.Empty;
        _zenithAiMessages.Add(new ZenithAiChatMessage("user", question));

        // 1. Instant local direct player control commands (zero API latency)
        var (handledPlayerCmd, playerReply) = await TryHandleZenithAiPlayerCommandAsync(question);
        if (handledPlayerCmd)
        {
            _zenithAiMessages.Add(new ZenithAiChatMessage("assistant", playerReply));
            RenderZenithAiTranscript(ZenithAiTranscriptPanel, ZenithAiTranscriptScrollViewer, GetZenithAiTranscriptWidth());
            RefreshZenithAiPlayerStrip();
            ZenithAiStatusTextBlock.Text = "Acción aplicada directamente sobre Zenith Audio.";
            return;
        }

        // 2. Instant local tone preset commands (zero API latency)
        if (TryHandleZenithAiToneCommand(question, out var localResponse))
        {
            _zenithAiMessages.Add(new ZenithAiChatMessage("assistant", localResponse));
            RenderZenithAiTranscript(ZenithAiTranscriptPanel, ZenithAiTranscriptScrollViewer, GetZenithAiTranscriptWidth());
            RefreshZenithAiPlayerStrip();
            ZenithAiStatusTextBlock.Text = "Perfil tonal aplicado localmente.";
            return;
        }

        RenderZenithAiTranscript(ZenithAiTranscriptPanel, ZenithAiTranscriptScrollViewer, GetZenithAiTranscriptWidth(), "ZenithAI está pensando...");
        ZenithAiSendButton.IsEnabled = false;
        ZenithAiCancelButton.IsEnabled = true;
        ZenithAiClearButton.IsEnabled = false;
        ZenithAiStatusTextBlock.Text = $"Consultando {_zenithAiClient.Settings.Provider} ({_zenithAiClient.Settings.Model})...";

        _zenithAiRequestCancellation?.Cancel();
        _zenithAiRequestCancellation?.Dispose();
        _zenithAiCancelRequestedByUser = false;
        _zenithAiRequestCancellation = new CancellationTokenSource();

        using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _zenithAiRequestCancellation.Token,
            timeoutCancellation.Token);
        using var progressCancellation = new CancellationTokenSource();
        var progressTask = UpdateZenithAiRequestProgressAsync(
            _zenithAiClient.Settings.Provider,
            _zenithAiClient.Settings.Model,
            progressCancellation.Token);
        WriteZenithAiDiagnostic($"request-start provider={_zenithAiClient.Settings.Provider} model={_zenithAiClient.Settings.Model} endpoint={FormatEndpointForDiagnostics(_zenithAiClient.Settings.Endpoint)}");

        try
        {
            var rawResponse = await _zenithAiClient.SendAsync(
                _zenithAiMessages,
                BuildZenithAiAudioContext(question),
                linkedCancellation.Token);

            var parsed = ZenithAiActionParser.Parse(rawResponse);
            var actionNotes = await ExecuteZenithAiActionsAsync(parsed.Actions);
            var finalMessage = parsed.CleanText;
            if (actionNotes.Count > 0)
            {
                var summaryBlock = string.Join("\n", actionNotes.Select(note => $"✓ {note}"));
                finalMessage = string.IsNullOrWhiteSpace(finalMessage)
                    ? summaryBlock
                    : $"{finalMessage}\n\n{summaryBlock}";
            }

            _zenithAiMessages.Add(new ZenithAiChatMessage("assistant", finalMessage));
            RenderZenithAiTranscript(ZenithAiTranscriptPanel, ZenithAiTranscriptScrollViewer, GetZenithAiTranscriptWidth());
            RefreshZenithAiPlayerStrip();
            ZenithAiStatusTextBlock.Text = actionNotes.Count > 0
                ? $"Listo · {actionNotes.Count} acción(es) aplicada(s) directamente en el reproductor."
                : $"Listo · {_zenithAiClient.Settings.Provider} ({_zenithAiClient.Settings.Model}).";
            WriteZenithAiDiagnostic($"request-ok actions={actionNotes.Count}");
        }
        catch (OperationCanceledException)
        {
            var message = _zenithAiCancelRequestedByUser
                ? "Consulta cancelada por el usuario."
                : timeoutCancellation.IsCancellationRequested
                    ? "La API no respondió dentro de 300 segundos. Prueba otro modelo en Config API o reduce la pregunta."
                    : "Consulta cancelada al cerrar ZenithAI.";
            _zenithAiMessages.Add(new ZenithAiChatMessage("assistant", message));
            RenderZenithAiTranscript(ZenithAiTranscriptPanel, ZenithAiTranscriptScrollViewer, GetZenithAiTranscriptWidth());
            ZenithAiStatusTextBlock.Text = message;
            WriteZenithAiDiagnostic($"request-canceled user={_zenithAiCancelRequestedByUser} timeout={timeoutCancellation.IsCancellationRequested}");
        }
        catch (Exception ex)
        {
            var provider = string.IsNullOrWhiteSpace(_zenithAiClient.Settings.Provider) ? "la API" : _zenithAiClient.Settings.Provider;
            _zenithAiMessages.Add(new ZenithAiChatMessage("assistant", $"No pude conectar con {provider}: {ex.Message}"));
            RenderZenithAiTranscript(ZenithAiTranscriptPanel, ZenithAiTranscriptScrollViewer, GetZenithAiTranscriptWidth());
            ZenithAiStatusTextBlock.Text = "Revisa conexión, API key, endpoint o modelo en Config API.";
            WriteZenithAiDiagnostic($"request-error {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            progressCancellation.Cancel();
            try
            {
                await progressTask;
            }
            catch (OperationCanceledException)
            {
            }

            ZenithAiSendButton.IsEnabled = true;
            ZenithAiCancelButton.IsEnabled = false;
            ZenithAiClearButton.IsEnabled = true;
            _zenithAiRequestCancellation?.Dispose();
            _zenithAiRequestCancellation = null;
            ScrollZenithAiTranscriptToEnd(ZenithAiTranscriptScrollViewer);
        }
    }

    private async Task<(bool Handled, string Response)> TryHandleZenithAiPlayerCommandAsync(string question)
    {
        var normalized = NormalizeCommandText(question);

        // Only handle crisp, unambiguous direct commands locally; let complex multi-part questions go to the LLM
        if (normalized.Length > 72 || normalized.Contains('?') ||
            ContainsAny(normalized, "explica", "historia", "diferencia", "por que", "porque", "que es", "recomienda", "cuentame"))
        {
            return (false, string.Empty);
        }

        if (normalized is "siguiente" or "siguiente pista" or "pista siguiente" or "next" or "cambia de cancion" or "otra cancion")
        {
            await PlayNextTrackAsync(manual: true);
            return (true, $"✓ Saltando a la siguiente pista: {TrackTitleTextBlock.Text}");
        }

        if (normalized is "anterior" or "pista anterior" or "anterior pista" or "prev" or "previous" or "vuelve a la anterior")
        {
            await PlayPreviousTrackAsync(manual: true);
            return (true, $"✓ Volviendo a la pista anterior: {TrackTitleTextBlock.Text}");
        }

        if (normalized is "pausa" or "pausar" or "pausa o reanuda" or "reanuda" or "reanudar" or "play" or "reproducir")
        {
            ZenithAiPlayPauseButton_Click(this, new RoutedEventArgs());
            return (true, _isPaused ? "✓ Reproducción pausada." : $"✓ Reproduciendo: {TrackTitleTextBlock.Text}");
        }

        if (normalized is "deten" or "detener" or "stop" or "para la musica")
        {
            if (StopButton.IsEnabled)
            {
                StopButton_Click(this, new RoutedEventArgs());
            }

            return (true, "✓ Reproducción detenida.");
        }

        if (ContainsAny(normalized, "cambia a asio", "usa asio", "modo asio", "pon asio", "activar asio"))
        {
            var note = await ExecuteSingleZenithAiActionAsync(new ZenithAiAction("backend", new Dictionary<string, string> { ["value"] = "asio" }));
            return (true, $"✓ {note}");
        }

        if (ContainsAny(normalized, "cambia a wasapi", "usa wasapi", "modo wasapi", "pon wasapi"))
        {
            var note = await ExecuteSingleZenithAiActionAsync(new ZenithAiAction("backend", new Dictionary<string, string> { ["value"] = "wasapi" }));
            return (true, $"✓ {note}");
        }

        if (ContainsAny(normalized, "abre panel asio", "panel asio", "control panel asio"))
        {
            var note = await ExecuteSingleZenithAiActionAsync(new ZenithAiAction("open_tab", new Dictionary<string, string> { ["value"] = "asio_panel" }));
            return (true, $"✓ {note}");
        }

        if (ContainsAny(normalized, "analiza pista", "anti fake", "antifake", "analizar pista"))
        {
            var note = await ExecuteSingleZenithAiActionAsync(new ZenithAiAction("analyze", new Dictionary<string, string>()));
            return (true, $"✓ {note}");
        }

        if (ContainsAny(normalized, "muestra albumes dsd", "muestra dsd", "ver dsd", "filtra dsd"))
        {
            var note = await ExecuteSingleZenithAiActionAsync(new ZenithAiAction("filter", new Dictionary<string, string> { ["value"] = "dsd" }));
            return (true, $"✓ {note}");
        }

        if (ContainsAny(normalized, "muestra hires", "muestra hi res", "ver hires", "filtra hires"))
        {
            var note = await ExecuteSingleZenithAiActionAsync(new ZenithAiAction("filter", new Dictionary<string, string> { ["value"] = "hires" }));
            return (true, $"✓ {note}");
        }

        var volMatch = System.Text.RegularExpressions.Regex.Match(normalized, @"(?:volumen|vol)\s*(?:a|al|en)?\s*(\d{1,3})");
        if (volMatch.Success && int.TryParse(volMatch.Groups[1].Value, out var volValue))
        {
            var note = await ExecuteSingleZenithAiActionAsync(new ZenithAiAction("volume", new Dictionary<string, string> { ["value"] = volValue.ToString(CultureInfo.InvariantCulture) }));
            return (true, $"✓ {note}");
        }

        var outputMatch = System.Text.RegularExpressions.Regex.Match(normalized, @"^(?:cambia|cambiar|pon|usa|selecciona)\s+(?:la\s+)?salida\s+(?:a\s+)?(.+)$");
        if (outputMatch.Success)
        {
            var query = outputMatch.Groups[1].Value.Trim();
            var note = await ExecuteSingleZenithAiActionAsync(new ZenithAiAction("output", new Dictionary<string, string> { ["query"] = query }));
            return (true, $"✓ {note}");
        }

        var playMatch = System.Text.RegularExpressions.Regex.Match(normalized, @"^(?:pon|reproduce|reproducir|toca)\s+(.+)$");
        if (playMatch.Success)
        {
            var query = playMatch.Groups[1].Value.Trim();
            if (!ContainsAny(query, "mas graves", "menos graves", "mas agudos", "menos agudos", "puro", "calido", "voces", "eq", "volumen", "asio", "wasapi"))
            {
                var note = await ExecuteSingleZenithAiActionAsync(new ZenithAiAction("play_track", new Dictionary<string, string> { ["query"] = query }));
                return (true, $"✓ {note}");
            }
        }

        return (false, string.Empty);
    }

    private async Task<List<string>> ExecuteZenithAiActionsAsync(IReadOnlyList<ZenithAiAction> actions)
    {
        var notes = new List<string>();
        foreach (var action in actions)
        {
            try
            {
                var note = await ExecuteSingleZenithAiActionAsync(action);
                if (!string.IsNullOrWhiteSpace(note))
                {
                    notes.Add(note);
                }
            }
            catch (Exception ex)
            {
                notes.Add($"No se pudo ejecutar '{action.Name}': {ex.Message}");
            }
        }

        return notes;
    }

    private async Task<string> ExecuteSingleZenithAiActionAsync(ZenithAiAction action)
    {
        action.Parameters.TryGetValue("value", out var value);
        action.Parameters.TryGetValue("query", out var query);
        value ??= string.Empty;
        query ??= value;

        switch (action.Name)
        {
            case "play_track":
            case "play":
                if (string.IsNullOrWhiteSpace(query))
                {
                    if (!string.IsNullOrWhiteSpace(_currentFilePath))
                    {
                        PlayButton_Click(this, new RoutedEventArgs());
                        return $"Reproduciendo pista actual: {TrackTitleTextBlock.Text}";
                    }

                    return "No hay pista cargada para reproducir.";
                }

                var matchedTrack = FindBestLibraryTrackMatch(query);
                if (matchedTrack is null)
                {
                    return $"No encontré una pista que coincida con '{query}' en tu biblioteca ({_libraryTracks.Count} pistas).";
                }

                StopCurrentPlaybackForTrackChange();
                await LoadLibraryTrackAsync(matchedTrack, play: true);
                return $"Reproduciendo '{matchedTrack.Title}' ({matchedTrack.Format}).";

            case "pause":
                if (StopButton.IsEnabled && !_isPaused)
                {
                    PauseButton_Click(this, new RoutedEventArgs());
                }

                return "Reproducción pausada.";

            case "resume":
                if (StopButton.IsEnabled && _isPaused)
                {
                    PauseButton_Click(this, new RoutedEventArgs());
                }
                else if (!string.IsNullOrWhiteSpace(_currentFilePath) && !StopButton.IsEnabled)
                {
                    PlayButton_Click(this, new RoutedEventArgs());
                }

                return "Reproducción reanudada.";

            case "stop":
                if (StopButton.IsEnabled)
                {
                    StopButton_Click(this, new RoutedEventArgs());
                }

                return "Reproducción detenida.";

            case "next":
                await PlayNextTrackAsync(manual: true);
                return $"Siguiente pista: {TrackTitleTextBlock.Text}";

            case "previous":
                await PlayPreviousTrackAsync(manual: true);
                return $"Pista anterior: {TrackTitleTextBlock.Text}";

            case "volume":
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var vol))
                {
                    var clamped = Math.Clamp(vol, 0, 100);
                    VolumeSlider.Value = clamped;
                    return $"Volumen ajustado a {(int)clamped}%.";
                }

                return "Valor de volumen inválido.";

            case "shuffle":
                _shuffleEnabled = value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                                  value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                  (value.Equals("toggle", StringComparison.OrdinalIgnoreCase) && !_shuffleEnabled);
                UpdateShuffleButtonVisual();
                return _shuffleEnabled ? "Modo aleatorio activado." : "Modo aleatorio desactivado.";

            case "seek":
                var secText = action.Parameters.TryGetValue("seconds", out var sVal) ? sVal : value;
                if (double.TryParse(secText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                {
                    var target = TimeSpan.FromSeconds(Math.Max(0, seconds));
                    SeekFallbackPlayer(target);
                    return $"Posición movida a {FormatTime(target)}.";
                }

                return "Posición de tiempo inválida.";

            case "backend":
                var normalizedBackend = value.Trim().ToLowerInvariant();
                var targetIndex = normalizedBackend switch
                {
                    "wasapi" or "zenith" or "zenithwasapi" => 0,
                    "asio" or "asionative" or "dsd" => 1,
                    "bass" or "basswasapi" => 2,
                    "mpv" or "mpvwasapi" => 3,
                    _ => -1
                };
                if (targetIndex < 0 || targetIndex >= BackendComboBox.Items.Count)
                {
                    return $"Motor '{value}' no reconocido.";
                }

                if (targetIndex == 1 && _asioDrivers.Count == 0)
                {
                    return "No se detectaron drivers ASIO en Windows para cambiar a ASIO.";
                }

                BackendComboBox.SelectedIndex = targetIndex;
                return $"Motor cambiado a {GetBackendShortLabel(GetSelectedBackend())}.";

            case "output":
                var devIdx = _outputDevices.FindIndex(d =>
                    d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    d.KindLabel.Contains(query, StringComparison.OrdinalIgnoreCase));
                if (devIdx < 0)
                {
                    return $"No encontré una salida de audio que coincida con '{query}'.";
                }

                OutputDeviceComboBox.SelectedIndex = devIdx;
                return $"Salida cambiada a {_outputDevices[devIdx].Name}.";

            case "exclusive":
                var enableExcl = value.Equals("on", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                if (enableExcl && !HasDedicatedDacDriver(GetSelectedOutputDevice()))
                {
                    return "No se activó Exclusivo porque la salida actual no es un controlador DAC USB dedicado.";
                }

                ExclusiveModeToggle.IsOn = enableExcl;
                return enableExcl ? "Modo Exclusivo activado." : "Modo Exclusivo desactivado.";

            case "dsd_mode":
                var modeIdx = value.ToLowerInvariant() switch
                {
                    "pcm" or "pcm_fir" or "fir" => 0,
                    "dop" => 1,
                    "native" or "nativo" => 2,
                    _ => -1
                };
                if (modeIdx >= 0 && modeIdx < DsdModeComboBox.Items.Count)
                {
                    DsdModeComboBox.SelectedIndex = modeIdx;
                    return $"Modo DSD actualizado ({(DsdModeComboBox.SelectedItem as ComboBoxItem)?.Content}).";
                }

                return "Modo DSD no reconocido.";

            case "dsd_gain":
                var gainIdx = value switch
                {
                    "0" => 0,
                    "3" => 1,
                    "6" => 2,
                    _ => -1
                };
                if (gainIdx >= 0 && DsdGainComboBox is not null)
                {
                    DsdGainComboBox.SelectedIndex = gainIdx;
                    return $"Ganancia DSD ajustada a +{value} dB.";
                }

                return "Ganancia DSD no reconocida (usa 0, 3 o 6).";

            case "dsd_rate":
                if (DsdPcmRateComboBox is not null)
                {
                    for (var i = 0; i < DsdPcmRateComboBox.Items.Count; i++)
                    {
                        if (DsdPcmRateComboBox.Items[i] is ComboBoxItem { Tag: string tag } && tag == value)
                        {
                            DsdPcmRateComboBox.SelectedIndex = i;
                            return $"Frecuencia PCM para DSD ajustada ({(DsdPcmRateComboBox.Items[i] as ComboBoxItem)?.Content}).";
                        }
                    }
                }

                return "Frecuencia PCM DSD no reconocida.";

            case "tone":
                var wantsBypass = action.Parameters.TryGetValue("bypass", out var bp) && bp.Equals("true", StringComparison.OrdinalIgnoreCase);
                if (wantsBypass || value.Equals("puro", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyToneProfile("Audio puro (ZenithAI)", eqEnabled: false, dspBypassed: true, preampDb: 0, subBassDb: 0, presenceDb: 0, airDb: 0);
                    return "DSP en bypass (audio plano / bit-transparente).";
                }

                var preamp = ParseParamDouble(action.Parameters, "preamp", 0, -12, 12);
                var sub = ParseParamDouble(action.Parameters, "sub", 0, -6, 6);
                var presence = ParseParamDouble(action.Parameters, "presence", 0, -6, 6);
                var air = ParseParamDouble(action.Parameters, "air", 0, -6, 6);
                var maxBoost = Math.Max(0, Math.Max(sub, Math.Max(presence, air)));
                if (preamp == 0 && maxBoost > 0)
                {
                    preamp = -Math.Ceiling(maxBoost * 2.0) / 2.0; // Automatic anti-clipping preamp
                }

                ApplyToneProfile("EQ personalizado (ZenithAI)", eqEnabled: true, dspBypassed: false, preamp, sub, presence, air);
                return $"EQ aplicado: Pre {FormatDb(preamp)}, Sub {FormatDb(sub)}, Presencia {FormatDb(presence)}, Aire {FormatDb(air)}.";

            case "filter":
                if (value.Equals("dsd", StringComparison.OrdinalIgnoreCase))
                {
                    ShowDsdLibraryButton_Click(this, new RoutedEventArgs());
                    return $"Biblioteca filtrada: {_visibleLibraryTracks.Count} pistas DSD.";
                }

                if (value.Equals("hires", StringComparison.OrdinalIgnoreCase))
                {
                    ShowHiResLibraryButton_Click(this, new RoutedEventArgs());
                    return $"Biblioteca filtrada: {_visibleLibraryTracks.Count} pistas Hi-Res PCM.";
                }

                if (value.Equals("folders", StringComparison.OrdinalIgnoreCase))
                {
                    ShowFolderBrowser();
                    return $"Mostrando {_libraryFolders.Count} carpeta(s) de biblioteca.";
                }

                if (value.StartsWith("search:", StringComparison.OrdinalIgnoreCase))
                {
                    var searchTerm = value["search:".Length..].Trim();
                    ShowTrackBrowser("Explorador de biblioteca", _libraryTracks);
                    LibrarySearchBox.Text = searchTerm;
                    ApplyLibrarySearch();
                    return $"Buscador de biblioteca filtrado por '{searchTerm}'.";
                }

                ShowTrackBrowser("Explorador de biblioteca", _libraryTracks);
                return $"Mostrando toda la biblioteca ({_libraryTracks.Count} pistas).";

            case "analyze":
                RefreshAntiFakeReport(showStatus: true);
                return $"Análisis Anti-Fake: {LabScoreTextBlock.Text} · {LabAuthenticityTextBlock.Text}";

            case "open_tab":
                switch (value.ToLowerInvariant())
                {
                    case "signal":
                        SelectRightPanelTab(0);
                        return "Panel derecho cambiado a Cadena de señal.";
                    case "tone":
                        SelectRightPanelTab(1);
                        return "Panel derecho cambiado a Control de tono / EQ.";
                    case "settings":
                        ShowSettingsButton_Click(this, new RoutedEventArgs());
                        return "Panel derecho cambiado a Ajustes del sistema.";
                    case "asio_panel":
                        OpenAsioControlPanelButton_Click(this, new RoutedEventArgs());
                        return "Panel de control ASIO abierto.";
                }

                return string.Empty;

            default:
                return string.Empty;
        }
    }

    private LibraryTrack? FindBestLibraryTrackMatch(string query)
    {
        if (_libraryTracks.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var normalizedQuery = NormalizeCommandText(query);
        var tokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        LibraryTrack? bestTrack = null;
        var bestScore = 0;

        foreach (var track in _libraryTracks)
        {
            if (track.Extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normTitle = NormalizeCommandText(track.Title);
            var normDir = NormalizeCommandText(Path.GetFileName(Path.GetDirectoryName(track.Path) ?? track.Folder));
            var score = 0;

            if (normTitle.Equals(normalizedQuery, StringComparison.Ordinal))
            {
                score += 100;
            }
            else if (normTitle.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                score += 50;
            }
            else if (normDir.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                score += 35;
            }

            foreach (var token in tokens)
            {
                if (token.Length < 2)
                {
                    continue;
                }

                if (normTitle.Contains(token, StringComparison.Ordinal))
                {
                    score += 12;
                }
                else if (normDir.Contains(token, StringComparison.Ordinal))
                {
                    score += 8;
                }
                else if (track.Format.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    score += 4;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestTrack = track;
            }
        }

        return bestScore >= 8 ? bestTrack : null;
    }

    private static double ParseParamDouble(IReadOnlyDictionary<string, string> parameters, string key, double fallback, double min, double max)
    {
        return parameters.TryGetValue(key, out var raw) &&
               double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;
    }

    private static string ZenithAiDiagnosticPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZenithAudio",
        "zenithai.log");

    private static void WriteZenithAiDiagnostic(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ZenithAiDiagnosticPath)!);
            File.AppendAllText(
                ZenithAiDiagnosticPath,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string FormatEndpointForDiagnostics(string endpoint)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}"
            : "endpoint-invalido";
    }

    private async Task UpdateZenithAiRequestProgressAsync(string provider, string model, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var elapsed = (int)Math.Max(1, (DateTimeOffset.Now - startedAt).TotalSeconds);
            ZenithAiStatusTextBlock.Text = $"Esperando respuesta de {provider} ({model})... {elapsed}s / 300s";
        }
    }

    private void ZenithAiDragHeader_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isDraggingZenithAiPanel = true;
        _zenithAiDragStartPoint = e.GetCurrentPoint(RootGrid).Position;
        _zenithAiDragStartX = ZenithAiPanelTransform.X;
        _zenithAiDragStartY = ZenithAiPanelTransform.Y;
        ZenithAiDragHeaderBorder.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ZenithAiDragHeader_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDraggingZenithAiPanel)
        {
            return;
        }

        var currentPoint = e.GetCurrentPoint(RootGrid).Position;
        ZenithAiPanelTransform.X = _zenithAiDragStartX + currentPoint.X - _zenithAiDragStartPoint.X;
        ZenithAiPanelTransform.Y = _zenithAiDragStartY + currentPoint.Y - _zenithAiDragStartPoint.Y;
        ClampZenithAiPanelPosition();
        e.Handled = true;
    }

    private void ZenithAiDragHeader_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        StopZenithAiPanelDrag();
        ZenithAiDragHeaderBorder.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void ZenithAiDragHeader_PointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        StopZenithAiPanelDrag();
    }

    private void ZenithAiDragHeader_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        StopZenithAiPanelDrag();
    }

    private void StopZenithAiPanelDrag()
    {
        _isDraggingZenithAiPanel = false;
    }

    private void ClampZenithAiPanelPosition()
    {
        var rootWidth = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : AppWindow.Size.Width;
        var rootHeight = RootGrid.ActualHeight > 0 ? RootGrid.ActualHeight : AppWindow.Size.Height;
        var maxX = Math.Max(0, (rootWidth - ZenithAiPanelBorder.Width) / 2 - 16);
        var maxY = Math.Max(0, (rootHeight - ZenithAiPanelBorder.Height) / 2 - 16);
        ZenithAiPanelTransform.X = Math.Clamp(ZenithAiPanelTransform.X, -maxX, maxX);
        ZenithAiPanelTransform.Y = Math.Clamp(ZenithAiPanelTransform.Y, -maxY, maxY);
    }

    private void ZenithAiTranscriptScrollViewer_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(ZenithAiTranscriptScrollViewer).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        var nextOffset = Math.Clamp(
            ZenithAiTranscriptScrollViewer.VerticalOffset - delta,
            0,
            ZenithAiTranscriptScrollViewer.ScrollableHeight);
        ZenithAiTranscriptScrollViewer.ChangeView(null, nextOffset, null, disableAnimation: true);
        e.Handled = true;
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

    private async void DsdModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusInfoBar is null)
        {
            return;
        }

        SaveAudioSettings();
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
        await HotSwitchActivePlaybackAsync();
    }

    private async void DsdPlaybackOption_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusInfoBar is null)
        {
            return;
        }

        SaveAudioSettings();
        UpdateSignalChain();
        await HotSwitchActivePlaybackAsync();
    }

    private async void ExclusiveModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isRevertingExclusiveToggle || _isLoadingAudioSettings || ModeTextBlock is null || OutputPathTextBlock is null)
        {
            return;
        }

        if (ExclusiveModeToggle.IsOn && !HasDedicatedDacDriver(GetSelectedOutputDevice()))
        {
            SetExclusiveToggleSilently(false);
            SaveAudioSettings();
            ApplyOutputDeviceSettings();
            UpdatePlaybackAvailabilityV2();
            UpdateSignalChain();
            await ShowMissingDacDriverAlertAsync(GetSelectedOutputDevice());
            return;
        }

        SaveAudioSettings();
        ApplyOutputDeviceSettings();
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
        await HotSwitchActivePlaybackAsync();
    }

    private static bool HasDedicatedDacDriver(OutputDeviceOption? device)
    {
        return device is not null && !device.IsSystemDefault && device.IsLikelyDac;
    }

    private void SetExclusiveToggleSilently(bool isOn)
    {
        if (ExclusiveModeToggle is null || ExclusiveModeToggle.IsOn == isOn)
        {
            return;
        }

        _isRevertingExclusiveToggle = true;
        try
        {
            ExclusiveModeToggle.IsOn = isOn;
        }
        finally
        {
            _isRevertingExclusiveToggle = false;
        }
    }

    private void EnforceExclusiveDriverGuard(bool showAlert)
    {
        if (ExclusiveModeToggle is null || !ExclusiveModeToggle.IsOn)
        {
            return;
        }

        var selectedDevice = GetSelectedOutputDevice();
        if (HasDedicatedDacDriver(selectedDevice))
        {
            return;
        }

        SetExclusiveToggleSilently(false);
        SaveAudioSettings();

        if (showAlert)
        {
            _ = ShowMissingDacDriverAlertAsync(selectedDevice);
        }
    }

    private async Task ShowMissingDacDriverAlertAsync(OutputDeviceOption? device)
    {
        if (_isShowingExclusiveAlert || RootGrid?.XamlRoot is null)
        {
            return;
        }

        _isShowingExclusiveAlert = true;
        try
        {
            var deviceName = device is null || device.IsSystemDefault
                ? "Salida predeterminada de Windows"
                : device.Name;

            var dialog = new ContentDialog
            {
                Title = "Controlador DAC dedicado no detectado",
                Content =
                    $"La salida actual ({deviceName}) no corresponde a un controlador DAC USB dedicado " +
                    "(por ejemplo: FiiO USB Audio Device, ZiShan, Topping, iFi, SMSL, XMOS, etc.).\n\n" +
                    "El Modo Exclusivo se ha desactivado automáticamente y permanecerá desactivado por defecto para evitar ruido blanco o incompatibilidades con salidas integradas (como el jack 3.5 mm).\n\n" +
                    "Cuando conectes tu DAC USB y lo selecciones en el selector de salida, podrás activar el Modo Exclusivo.",
                CloseButtonText = "Entendido",
                XamlRoot = RootGrid.XamlRoot
            };

            await dialog.ShowAsync();
        }
        catch
        {
        }
        finally
        {
            _isShowingExclusiveAlert = false;
        }
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
            ? "Control de tono activo en tiempo real en Zenith WASAPI (DSD->PCM FIR, FLAC, WAV, AIFF, MP3). En DoP nativo el DSP se omite."
            : "Control de tono en bypass: salida bit-transparente.";
    }

    private bool TryHandleZenithAiToneCommand(string question, out string response)
    {
        response = string.Empty;
        var normalized = NormalizeCommandText(question);
        var wantsToneAction =
            ContainsAny(normalized, "ajusta", "ajustar", "cambia", "cambiar", "modifica", "modificar", "pon", "poner", "quiero", "dame", "activa", "activar", "perfil", "sonido", "audio", "eq", "tono") &&
            ContainsAny(normalized, "puro", "plano", "natural", "bit perfect", "grave", "graves", "bajo", "bajos", "subgrave", "agudo", "agudos", "brillo", "aire", "detalle", "analitico", "calido", "voces", "voz", "rock", "jazz", "clasica", "automatico", "auto", "musica", "pista", "cancion", "recomendado", "mejor");

        if (!wantsToneAction)
        {
            return false;
        }

        var profile = ResolveZenithAiToneProfile(normalized);
        ApplyToneProfile(
            profile.Name,
            profile.EqEnabled,
            profile.DspBypassed,
            profile.PreampDb,
            profile.SubBassDb,
            profile.PresenceDb,
            profile.AirDb);

        response =
            $"Perfil aplicado: {profile.Name}\n\n" +
            $"{profile.Description}\n\n" +
            $"Ajustes: Preamp {FormatDb(profile.PreampDb)}, Subgraves {FormatDb(profile.SubBassDb)}, Presencia {FormatDb(profile.PresenceDb)}, Aire {FormatDb(profile.AirDb)}. " +
            $"DSP: {(profile.DspBypassed ? "bypass" : "activo")}.\n\n" +
            "Puedes pedirme: audio mas puro, mas graves, menos graves, mas agudos, mas calido, mas detalle, voces al frente o perfil automatico.";
        return true;
    }

    private ZenithAiToneProfile ResolveZenithAiToneProfile(string normalizedQuestion)
    {
        if (ContainsAny(normalizedQuestion, "puro", "plano", "natural", "bit perfect", "sin dsp", "bypass"))
        {
            return new ZenithAiToneProfile(
                "Pureza bit-perfect",
                "Dejo el control de tono en bypass para priorizar una salida plana y transparente.",
                false,
                true,
                0,
                0,
                0,
                0);
        }

        if (ContainsAny(normalizedQuestion, "menos graves", "menos bajos", "bajar graves", "reducir graves"))
        {
            return new ZenithAiToneProfile(
                "Graves contenidos",
                "Reduzco subgraves para limpiar mezcla, sala cargada o audifonos con exceso de bajo.",
                true,
                false,
                -1,
                -2.5,
                0.5,
                0.5);
        }

        if (ContainsAny(normalizedQuestion, "grave", "graves", "bajo", "bajos", "subgrave", "subgraves"))
        {
            return new ZenithAiToneProfile(
                "Graves audifilos",
                "Subo el fundamento grave con preamp negativo para evitar clipping digital.",
                true,
                false,
                -3,
                3,
                0.5,
                0.5);
        }

        if (ContainsAny(normalizedQuestion, "menos agudos", "menos brillo", "suavizar agudos", "bajar agudos"))
        {
            return new ZenithAiToneProfile(
                "Agudos suaves",
                "Reduzco presencia y aire para controlar sibilancia o grabaciones brillantes.",
                true,
                false,
                -1,
                0.5,
                -1,
                -2.5);
        }

        if (ContainsAny(normalizedQuestion, "agudo", "agudos", "brillo", "aire"))
        {
            return new ZenithAiToneProfile(
                "Aire y extension",
                "Realzo presencia y aire para dar mas definicion sin tocar demasiado el grave.",
                true,
                false,
                -2.5,
                0,
                1.5,
                3);
        }

        if (ContainsAny(normalizedQuestion, "voz", "voces", "vocal", "cantante"))
        {
            return new ZenithAiToneProfile(
                "Voces al frente",
                "Empujo la zona de presencia para que voces, guitarras y solistas queden mas legibles.",
                true,
                false,
                -2,
                0.5,
                2.5,
                1);
        }

        if (ContainsAny(normalizedQuestion, "calido", "calida", "suave", "relajado"))
        {
            return new ZenithAiToneProfile(
                "Calidez analogica",
                "Aumento levemente el cuerpo y bajo el aire para una escucha mas relajada.",
                true,
                false,
                -2,
                2,
                -0.5,
                -1);
        }

        if (ContainsAny(normalizedQuestion, "detalle", "analitico", "analitica", "resolucion", "separacion"))
        {
            return new ZenithAiToneProfile(
                "Detalle analitico",
                "Aumento presencia y aire con margen de preamp para mejorar lectura de microdetalle.",
                true,
                false,
                -2.5,
                0,
                1.5,
                2.5);
        }

        if (ContainsAny(normalizedQuestion, "rock", "metal"))
        {
            return new ZenithAiToneProfile(
                "Rock dinamico",
                "Refuerzo pegada y presencia para bateria, bajo y guitarras sin saturar la salida.",
                true,
                false,
                -3,
                2.5,
                1.5,
                1);
        }

        if (ContainsAny(normalizedQuestion, "jazz", "acustico", "acustica"))
        {
            return new ZenithAiToneProfile(
                "Jazz acustico",
                "Perfil sutil para contrabajo, metales y sala, manteniendo una presentacion natural.",
                true,
                false,
                -1.5,
                1,
                1,
                1.5);
        }

        if (ContainsAny(normalizedQuestion, "clasica", "orquesta", "sinfonica", "piano"))
        {
            return new ZenithAiToneProfile(
                "Escena clasica",
                "Ajuste leve para aire y escena, con minimo procesamiento.",
                true,
                false,
                -1,
                0.5,
                0.5,
                1.5);
        }

        return ResolveAutomaticToneProfile();
    }

    private ZenithAiToneProfile ResolveAutomaticToneProfile()
    {
        var extension = Path.GetExtension(GetEffectivePlaybackPath(_currentFilePath ?? string.Empty)).ToLowerInvariant();

        if (DsdExtensions.Contains(extension))
        {
            return new ZenithAiToneProfile(
                "Automatico DSD puro",
                "La pista actual es DSD/DSF/DFF/ISO. Priorizo bypass DSP para conservar la lectura mas transparente posible.",
                false,
                true,
                0,
                0,
                0,
                0);
        }

        if (extension is ".mp3" or ".aac" or ".m4a" or ".ogg" or ".opus")
        {
            return new ZenithAiToneProfile(
                "Automatico con recuperacion suave",
                "La pista parece comprimida o con perdida. Uso un realce moderado de presencia y aire, con preamp seguro.",
                true,
                false,
                -2.5,
                1,
                1,
                1.5);
        }

        return new ZenithAiToneProfile(
            "Automatico Hi-Res neutral",
            "Uso un ajuste muy leve para mantener caracter natural y evitar alterar demasiado el master.",
            true,
            false,
            -1,
            0.5,
            0.5,
            0.75);
    }

    private void ApplyToneProfile(
        string profileName,
        bool eqEnabled,
        bool dspBypassed,
        double preampDb,
        double subBassDb,
        double presenceDb,
        double airDb)
    {
        EqToggleButton.IsChecked = eqEnabled;
        DspBypassToggleButton.IsChecked = dspBypassed;
        PreampSlider.Value = preampDb;
        SubBassSlider.Value = subBassDb;
        PresenceSlider.Value = presenceDb;
        AirSlider.Value = airDb;
        EqPresetComboBox.SelectedIndex = 0;
        UpdateToneControlState(showStatus: true);
        StatusInfoBar.Severity = dspBypassed ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        StatusInfoBar.Message = $"ZenithAI aplico el perfil tonal: {profileName}.";
    }

    private static string NormalizeCommandText(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
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

    private async void UseWindowsMusicButton_Click(object sender, RoutedEventArgs e)
    {
        var musicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrWhiteSpace(musicPath) || !Directory.Exists(musicPath))
        {
            var fallbackMusicPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Music");
            if (Directory.Exists(fallbackMusicPath))
            {
                musicPath = fallbackMusicPath;
            }
        }

        if (string.IsNullOrWhiteSpace(musicPath) || !Directory.Exists(musicPath))
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = "No se encontró la carpeta Música de Windows";
            return;
        }

        await AddLibraryFolderAsync(musicPath);
    }

    private async void AddMusicFolderButton_Click(object sender, RoutedEventArgs e)
    {
        await PickAndAddMusicFolderAsync();
    }

    private async void ShowMusicFoldersButton_Click(object sender, RoutedEventArgs e)
    {
        if (_libraryFolders.Count == 0)
        {
            var added = await PickAndAddMusicFolderAsync();
            if (!added)
            {
                ShowFolderBrowser();
            }

            return;
        }

        ShowFolderBrowser();
    }

    private async Task<bool> PickAndAddMusicFolderAsync()
    {
        try
        {
            var folderPath = await PickMusicFolderPathAsync();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return false;
            }

            await AddLibraryFolderAsync(folderPath);
            return true;
        }
        catch (Exception ex)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = $"No se pudo abrir el selector de carpetas: {ex.Message}";
            return false;
        }
    }

    private async Task<string?> PickMusicFolderPathAsync()
    {
        try
        {
            var modernPicker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id)
            {
                SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.MusicLibrary
            };
            var picked = await modernPicker.PickSingleFolderAsync();
            return picked?.Path;
        }
        catch (Exception)
        {
            var legacyPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary
            };
            legacyPicker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(legacyPicker, WindowNative.GetWindowHandle(this));

            var folder = await legacyPicker.PickSingleFolderAsync();
            return folder?.Path;
        }
    }

    private async void LibraryFoldersListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is string folderPath && !string.IsNullOrWhiteSpace(folderPath))
        {
            await OpenLibraryFolderAsync(folderPath);
        }
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
        SelectRightPanelTab(2);
        var width = _lastLayoutWidth > 0 ? _lastLayoutWidth : RootGrid.ActualWidth;
        if (width < 780)
        {
            _compactPaneMode = 2;
            ApplyResponsiveLayout(width, _lastLayoutHeight > 0 ? _lastLayoutHeight : RootGrid.ActualHeight);
        }
        else if (width < 1120)
        {
            _showInspectorInNarrow = true;
            ApplyResponsiveLayout(width, _lastLayoutHeight > 0 ? _lastLayoutHeight : RootGrid.ActualHeight);
        }

        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = "Ajustes de salida, ASIO, DSD y biblioteca abiertos en el panel derecho.";
    }

    private void LibrarySearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        ApplyLibrarySearch();
    }

    private async void LibraryBrowserListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LibraryBrowserItem item)
        {
            if (item.IsHeader)
            {
                LibraryBrowserListView.SelectedItem = null;
                return;
            }

            if (item.Track is { } clickedTrack)
            {
                LoadLibraryTrack(clickedTrack, play: false);
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.FolderPath))
            {
                await OpenLibraryFolderAsync(item.FolderPath);
                return;
            }
        }

        if (e.ClickedItem is LibraryTrack track)
        {
            LoadLibraryTrack(track, play: false);
            return;
        }

        if (e.ClickedItem is string folder)
        {
            await OpenLibraryFolderAsync(folder);
        }
    }

    private async void LibraryBrowserListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (LibraryBrowserListView.SelectedItem is LibraryBrowserItem { FolderPath: { Length: > 0 } folderPath, IsHeader: false })
        {
            await OpenLibraryFolderAsync(folderPath);
            return;
        }

        var selectedTrack = LibraryBrowserListView.SelectedItem switch
        {
            LibraryBrowserItem { Track: { } itemTrack, IsHeader: false } => itemTrack,
            LibraryTrack track => track,
            _ => null
        };

        if (selectedTrack is null)
        {
            return;
        }

        StopCurrentPlaybackForTrackChange();
        await LoadLibraryTrackAsync(selectedTrack, play: true);

        if (_lastLayoutWidth > 0 && _lastLayoutWidth < 780)
        {
            _compactPaneMode = 0;
            ApplyResponsiveLayout(_lastLayoutWidth, _lastLayoutHeight);
        }
    }

    private void LoadLibraryTrack(LibraryTrack track, bool play)
    {
        _ = LoadLibraryTrackAsync(track, play);
    }

    private async Task LoadLibraryTrackAsync(LibraryTrack track, bool play)
    {
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
        if (play)
        {
            PlayButton_Click(this, null!);
        }
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
            var metadata = string.IsNullOrWhiteSpace(_currentFilePath)
                ? AudioFileMetadata.Empty
                : InspectAudioFile(GetEffectivePlaybackPath(_currentFilePath));
            ApplySignalLabels(signal, metadata);
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
        if (_isSeeking)
        {
            return;
        }

        TimeSpan position;
        TimeSpan duration;

        if (_usingAsioEngine)
        {
            position = _asioEngine.Position;
            duration = _asioEngine.Duration;
        }
        else if (_usingWasapiEngine)
        {
            position = _wasapiEngine.Position;
            duration = _wasapiEngine.Duration;
        }
        else if (_usingFallbackPlayer)
        {
            var session = _fallbackMediaPlayer.PlaybackSession;
            position = session.Position;
            duration = session.NaturalDuration;
        }
        else
        {
            return;
        }

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
        if (_usingAsioEngine)
        {
            var asioDuration = _asioEngine.Duration;
            if (asioDuration <= TimeSpan.Zero)
            {
                return;
            }

            var clamped = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, asioDuration.TotalSeconds));
            _asioEngine.Seek(clamped);
            CurrentTimeTextBlock.Text = FormatTime(clamped);
            return;
        }

        if (_usingWasapiEngine)
        {
            var wasapiDuration = _wasapiEngine.Duration;
            if (wasapiDuration <= TimeSpan.Zero)
            {
                return;
            }

            var clamped = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, wasapiDuration.TotalSeconds));
            _wasapiEngine.Seek(clamped);
            CurrentTimeTextBlock.Text = FormatTime(clamped);
            return;
        }

        if (!_usingFallbackPlayer)
        {
            _audioEngine.Seek(position);
            CurrentTimeTextBlock.Text = FormatTime(position);
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
            if (_usingAsioEngine && _asioEngine.ActiveReport is { } asioReport)
            {
                decode = asioReport.DecodeLabel;
            }
            else if (_usingWasapiEngine && _wasapiEngine.ActiveReport is { } activeReport)
            {
                decode = activeReport.DecodeLabel;
            }
            else if (backend == AudioBackend.AsioNative)
            {
                var asioDriver = GetSelectedAsioDriver()?.Name ?? "ASIO";
                decode = DsdExtensions.Contains(currentExtension)
                    ? $"ASIO DSD Nativo ({asioDriver})"
                    : $"ASIO PCM ({asioDriver})";
            }
            else if (_usingFallbackPlayer && DsdExtensions.Contains(currentExtension))
            {
                decode = "DSD -> PCM FIR (stream)";
            }
            else if (_usingFallbackPlayer)
            {
                decode = "Windows Media Foundation";
            }
            else if (backend == AudioBackend.ZenithWasapi)
            {
                if (DsdExtensions.Contains(currentExtension))
                {
                    var dsdOptions = GetCurrentDsdPlaybackOptions();
                    decode = dsdOptions.OutputMode == DsdOutputMode.DoPExclusive
                        ? "Zenith WASAPI (DoP / FIR)"
                        : "Zenith WASAPI (DSD->PCM FIR)";
                }
                else
                {
                    decode = "Zenith WASAPI integrado";
                }
            }
            else if (IsNativeBackendAvailable(backend, effectivePath))
            {
                decode = backend == AudioBackend.MpvWasapi ? "MPV WASAPI" : "BASS WASAPI";
            }
            else if (DsdExtensions.Contains(currentExtension))
            {
                decode = "Zenith WASAPI (DSD->PCM FIR)";
            }
            else
            {
                decode = "Zenith WASAPI integrado";
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
        RefreshZenithAiPlayerStrip();
    }

    private void UpdateSignalVisualState(bool hasTrack)
    {
        var status = SignalBitPerfectTextBlock?.Text ?? string.Empty;
        var strict = status.Contains("Bit-perfect confirmado", StringComparison.OrdinalIgnoreCase) ||
                     status.Contains("Probable bit-perfect", StringComparison.OrdinalIgnoreCase);
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
        RefreshAntiFakeReport(showStatus: true);
    }

    private void RefreshAntiFakeReport(bool showStatus)
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

        if (showStatus)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = $"Laboratorio Anti-Fake: reporte generado para {report.Format}. Indice preliminar {report.Score}/100.";
        }

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

        if (showStatus)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = "Laboratorio Anti-Fake: análisis preliminar completado con metadatos. Espectrograma FFT real queda preparado como siguiente módulo.";
        }
    }

    private AntiFakeReport BuildAntiFakeReport(string filePath)
    {
        var analysisPath = GetEffectivePlaybackPath(filePath);
        var metadata = InspectAudioFile(analysisPath);
        var extension = GetAnalysisExtension(filePath, analysisPath).ToLowerInvariant();
        var extensionLabel = extension.TrimStart('.').ToUpperInvariant();
        var codec = CleanMetric(metadata.Codec, CleanMetric(CodecTextBlock.Text, extensionLabel));
        var sampleRate = CleanMetric(FormatSampleRate(metadata), CleanMetric(SampleRateTextBlock.Text, "-- kHz"));
        var bitDepth = CleanMetric(FormatBitDepth(metadata), CleanMetric(BitDepthTextBlock.Text, "-- bit"));
        var bitrate = CleanMetric(FormatBitrate(metadata), CleanMetric(BitrateTextBlock.Text, "-- kbps"));
        var sizeLabel = GetFileSizeLabel(File.Exists(analysisPath) ? analysisPath : filePath);
        var outputStatus = SignalBitPerfectTextBlock?.Text ?? "Sin validar";
        var dspStatus = _toneSettings.IsActive ? "DSP/EQ activo" : "DSP en bypass";

        if (IsLossyFormat(extension, codec))
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
            var dsdLabel = DetectDsdRateLabel(analysisPath);
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

        if (extension is ".flac" or ".wav" or ".aiff" or ".aif" or ".alac" or ".m4a" or ".ape" or ".wv" or ".mqa")
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
        return string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal) ? fallback : value;
    }

    private void ApplySignalMetadataForTrack(string filePath)
    {
        var metadata = InspectAudioFile(GetEffectivePlaybackPath(filePath));
        ApplySignalLabels(null, metadata);
    }

    private void ApplySignalLabels(AudioSignalInfo? signal, AudioFileMetadata metadata)
    {
        var merged = metadata;
        if (signal is not null)
        {
            merged = metadata with
            {
                SampleRate = signal.SampleRate > 0 ? signal.SampleRate : metadata.SampleRate,
                BitDepth = signal.BitDepth > 0 ? signal.BitDepth : metadata.BitDepth,
                Channels = signal.Channels > 0 ? signal.Channels : metadata.Channels,
                BitrateKbps = signal.BitrateKbps > 0 ? signal.BitrateKbps : metadata.BitrateKbps,
                IsDsd = signal.IsDsd || metadata.IsDsd,
                Codec = string.IsNullOrWhiteSpace(signal.Codec) ? metadata.Codec : signal.Codec
            };
        }

        BitrateTextBlock.Text = FormatBitrate(merged);
        SampleRateTextBlock.Text = FormatSampleRate(merged);
        BitDepthTextBlock.Text = FormatBitDepth(merged);
        ChannelsTextBlock.Text = merged.Channels > 0 ? merged.Channels.ToString(CultureInfo.InvariantCulture) : "--";
        CodecTextBlock.Text = string.IsNullOrWhiteSpace(merged.Codec) ? "Desconocido" : merged.Codec;
    }

    private static AudioFileMetadata InspectAudioFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return AudioFileMetadata.Empty;
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var metadata = AudioFileMetadata.Empty with
        {
            Codec = GetDefaultCodecLabel(extension),
            IsDsd = extension is ".dsf" or ".dff"
        };

        if (!File.Exists(filePath))
        {
            return metadata;
        }

        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var properties = tagFile.Properties;
            metadata = metadata with
            {
                Codec = GetCodecLabel(extension, properties),
                SampleRate = properties.AudioSampleRate,
                BitDepth = properties.BitsPerSample,
                Channels = properties.AudioChannels,
                BitrateKbps = properties.AudioBitrate,
                IsDsd = metadata.IsDsd
            };
        }
        catch (Exception)
        {
        }

        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) &&
            (metadata.SampleRate <= 0 || metadata.BitDepth <= 0 || metadata.Channels <= 0))
        {
            metadata = InspectWaveFile(filePath, metadata);
        }

        return metadata;
    }

    private static AudioFileMetadata InspectWaveFile(string filePath, AudioFileMetadata fallback)
    {
        try
        {
            using var reader = new NAudio.Wave.WaveFileReader(filePath);
            return fallback with
            {
                Codec = string.IsNullOrWhiteSpace(fallback.Codec) ? "PCM WAV" : fallback.Codec,
                SampleRate = fallback.SampleRate > 0 ? fallback.SampleRate : reader.WaveFormat.SampleRate,
                BitDepth = fallback.BitDepth > 0 ? fallback.BitDepth : reader.WaveFormat.BitsPerSample,
                Channels = fallback.Channels > 0 ? fallback.Channels : reader.WaveFormat.Channels,
                BitrateKbps = fallback.BitrateKbps > 0
                    ? fallback.BitrateKbps
                    : (reader.WaveFormat.AverageBytesPerSecond * 8 / 1000)
            };
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static string GetCodecLabel(string extension, TagLib.Properties properties)
    {
        var codec = properties.Codecs.FirstOrDefault()?.Description;
        if (!string.IsNullOrWhiteSpace(codec))
        {
            return codec;
        }

        return GetDefaultCodecLabel(extension);
    }

    private static string GetDefaultCodecLabel(string extension)
    {
        return extension switch
        {
            ".flac" => "FLAC",
            ".wav" => "PCM WAV",
            ".aiff" or ".aif" => "AIFF PCM",
            ".alac" or ".m4a" => "ALAC/AAC",
            ".ape" => "Monkey's Audio",
            ".wv" => "WavPack",
            ".opus" => "Opus",
            ".ogg" => "Ogg",
            ".mp3" => "MP3",
            ".aac" => "AAC",
            ".mqa" => "MQA/FLAC",
            ".dsf" or ".dff" => "DSD",
            ".iso" => "SACD ISO",
            _ => extension.TrimStart('.').ToUpperInvariant()
        };
    }

    private static string FormatSampleRate(AudioFileMetadata metadata)
    {
        if (metadata.SampleRate <= 0)
        {
            return metadata.IsDsd ? "DSD" : "-- kHz";
        }

        if (metadata.IsDsd)
        {
            return $"{metadata.SampleRate / 1_000_000.0:N4} MHz";
        }

        return $"{metadata.SampleRate / 1000.0:N1} kHz";
    }

    private static string FormatBitDepth(AudioFileMetadata metadata)
    {
        if (metadata.IsDsd)
        {
            return "1-bit DSD";
        }

        return metadata.BitDepth > 0 ? $"{metadata.BitDepth} bit" : "-- bit";
    }

    private static string FormatBitrate(AudioFileMetadata metadata)
    {
        return metadata.BitrateKbps > 0 ? $"{metadata.BitrateKbps:N0} kbps" : "-- kbps";
    }

    private static string GetAnalysisExtension(string originalPath, string analysisPath)
    {
        var analysisExtension = Path.GetExtension(analysisPath);
        if (!string.IsNullOrWhiteSpace(analysisExtension))
        {
            return analysisExtension;
        }

        return Path.GetExtension(originalPath);
    }

    private static bool IsLossyFormat(string extension, string codec)
    {
        if (extension is ".mp3" or ".aac" or ".opus")
        {
            return true;
        }

        if (extension is ".ogg")
        {
            return !codec.Contains("FLAC", StringComparison.OrdinalIgnoreCase);
        }

        if (extension is ".m4a")
        {
            return !codec.Contains("ALAC", StringComparison.OrdinalIgnoreCase) &&
                   !codec.Contains("Apple Lossless", StringComparison.OrdinalIgnoreCase);
        }

        return false;
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

    private sealed record AudioFileMetadata(
        string Codec,
        int SampleRate,
        int BitDepth,
        int Channels,
        int BitrateKbps,
        bool IsDsd)
    {
        public static AudioFileMetadata Empty { get; } = new(string.Empty, 0, 0, 0, 0, false);
    }

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
        if (_usingAsioEngine && _asioEngine.ActiveReport is { } asioReport)
        {
            if (asioReport.IsNativeDsd)
            {
                return $"Bit-perfect confirmado: ASIO {asioReport.SourceCodec} Nativo ({asioReport.SampleRate / 1_000_000.0:0.##} MHz / 1-bit)";
            }

            if (asioReport.IsBitPerfect)
            {
                return $"Bit-perfect confirmado: ASIO PCM ({asioReport.SampleRate / 1000.0:0.#} kHz/{asioReport.BitDepth}-bit)";
            }

            if (_toneSettings.IsActive)
            {
                return "No bit-perfect: DSP/EQ activo en ASIO";
            }

            if (asioReport.IsResampled)
            {
                return $"No bit-perfect: ASIO remuestreado a {asioReport.SampleRate / 1000.0:0.#} kHz";
            }

            return $"ASIO PCM ({asioReport.SampleRate / 1000.0:0.#} kHz/{asioReport.BitDepth}-bit)";
        }

        if (_usingWasapiEngine && _wasapiEngine.ActiveReport is { } report)
        {
            if (report.IsDoP)
            {
                return "Bit-perfect confirmado: DoP v1.1 (DSD sobre PCM exclusivo)";
            }

            if (report.IsBitPerfect)
            {
                return $"Bit-perfect confirmado: WASAPI Exclusivo ({report.OutputSampleRate / 1000.0:0.#} kHz/{report.OutputBitDepth}-bit)";
            }

            if (_toneSettings.IsActive)
            {
                return "No bit-perfect: DSP/EQ activo";
            }

            if (report.IsDsdSource)
            {
                return report.IsExclusive
                    ? $"DSD -> PCM FIR exclusivo ({report.OutputSampleRate / 1000.0:0.#} kHz/{report.OutputBitDepth}-bit)"
                    : $"No bit-perfect: DSD convertido a PCM compartido ({report.OutputSampleRate / 1000.0:0.#} kHz)";
            }

            if (report.IsResampled)
            {
                return $"No bit-perfect: remuestreado a {report.OutputSampleRate / 1000.0:0.#} kHz";
            }

            return report.IsExclusive
                ? "No bit-perfect: volumen software distinto de 100%"
                : "No confirmado: WASAPI modo compartido";
        }

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

        if (decode?.Contains("Zenith WASAPI", StringComparison.OrdinalIgnoreCase) == true ||
            decode?.Contains("nativa", StringComparison.OrdinalIgnoreCase) == true ||
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
            var coverMime = coverPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            UpdateCachedCoverBytes(await File.ReadAllBytesAsync(coverPath), coverMime);
            await ApplyDynamicAccentFromFileAsync(coverPath);
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
            UpdateCachedCoverBytes(bytes, picture.MimeType);
            await ApplyDynamicAccentFromBytesAsync(bytes);
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
        UpdateCachedCoverBytes(null, null);
        ApplyDynamicAccent(GetDefaultOledBlueAccent());
    }

    private async Task ApplyDynamicAccentFromFileAsync(string imagePath)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(imagePath);
            using var stream = await file.OpenReadAsync();
            var color = await ExtractDominantColorAsync(stream);
            ApplyDynamicAccent(color);
        }
        catch (Exception)
        {
        }
    }

    private async Task ApplyDynamicAccentFromBytesAsync(byte[] bytes)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
            }

            stream.Seek(0);
            var color = await ExtractDominantColorAsync(stream);
            ApplyDynamicAccent(color);
        }
        catch (Exception)
        {
        }
    }

    private static async Task<Windows.UI.Color> ExtractDominantColorAsync(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1, Math.Min(decoder.PixelWidth, 96)),
            ScaledHeight = Math.Max(1, Math.Min(decoder.PixelHeight, 96)),
            InterpolationMode = BitmapInterpolationMode.Linear
        };
        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var pixels = data.DetachPixelData();
        var red = 0.0;
        var green = 0.0;
        var blue = 0.0;
        var weightTotal = 0.0;

        for (var i = 0; i + 3 < pixels.Length; i += 16)
        {
            var r = pixels[i];
            var g = pixels[i + 1];
            var b = pixels[i + 2];
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var luminance = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
            if (luminance < 28 || luminance > 232 || max - min < 18)
            {
                continue;
            }

            var saturationWeight = 1.0 + ((max - min) / 255.0);
            var balancedWeight = saturationWeight * (1.0 - Math.Abs(luminance - 128.0) / 180.0);
            red += r * balancedWeight;
            green += g * balancedWeight;
            blue += b * balancedWeight;
            weightTotal += balancedWeight;
        }

        if (weightTotal <= 0.01)
        {
            return GetDefaultOledBlueAccent();
        }

        var dominant = Microsoft.UI.ColorHelper.FromArgb(
            255,
            (byte)Math.Clamp(red / weightTotal, 64, 224),
            (byte)Math.Clamp(green / weightTotal, 64, 224),
            (byte)Math.Clamp(blue / weightTotal, 64, 224));
        return ToOledBlueAccent(dominant);
    }

    private void ApplyDynamicAccent(Windows.UI.Color accent)
    {
        accent = ToOledBlueAccent(accent);
        _dynamicAccentColor = accent;
        var accentBrush = new SolidColorBrush(accent);
        var softBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(82, accent.R, accent.G, accent.B));
        var borderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(154, accent.R, accent.G, accent.B));
        var panelBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 1, 4, 9));
        var playbackBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 2, 6, 11));

        PlaybackProgressSlider.Foreground = accentBrush;
        ExclusiveModeToggle.Foreground = accentBrush;
        CoverGlowBorder.Background = softBrush;
        CoverCardBorder.BorderBrush = borderBrush;
        MainListeningPanelBorder.Background = panelBrush;
        MainListeningPanelBorder.BorderBrush = borderBrush;
        PlaybackBarBorder.Background = playbackBrush;
        PlaybackBarBorder.BorderBrush = borderBrush;

        ExclusiveModeToggle.Resources["ToggleSwitchFillOn"] = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(230, accent.R, accent.G, accent.B));
        ExclusiveModeToggle.Resources["ToggleSwitchFillOnPointerOver"] = accentBrush;
        ExclusiveModeToggle.Resources["ToggleSwitchStrokeOn"] = borderBrush;

        foreach (var bar in GetMiniSpectrumBars())
        {
            bar.Fill = accentBrush;
        }
    }

    private static Windows.UI.Color GetDefaultOledBlueAccent()
    {
        return Microsoft.UI.ColorHelper.FromArgb(255, 76, 184, 255);
    }

    private static Windows.UI.Color ToOledBlueAccent(Windows.UI.Color source)
    {
        var red = (byte)Math.Clamp((source.R * 0.16) + 16, 16, 82);
        var green = (byte)Math.Clamp((source.G * 0.36) + (source.B * 0.14) + 92, 108, 218);
        var blue = (byte)Math.Clamp((source.B * 0.55) + (source.G * 0.18) + 122, 170, 255);

        if (blue <= green)
        {
            blue = (byte)Math.Clamp(green + 28, 170, 255);
        }

        return Microsoft.UI.ColorHelper.FromArgb(255, red, green, blue);
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

        var lyricPath = FindLyricFile(filePath, folder, name);

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
            SyncLyricsTab();
        }
        catch (Exception)
        {
            ShowNoLyrics();
            LyricsStatusTextBlock.Text = "error";
            SyncLyricsTab();
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
            SyncLyricsTab();
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
        SyncLyricsTab();
    }

    private void SyncLyricsTab()
    {
        if (LyricsTabStatusTextBlock is null ||
            LyricsTabCurrentLineTextBlock is null ||
            LyricsTabTextBlock is null)
        {
            return;
        }

        LyricsTabStatusTextBlock.Text = LyricsStatusTextBlock.Text;
        LyricsTabCurrentLineTextBlock.Text = CurrentLyricTextBlock.Text;
        LyricsTabTextBlock.Text = LyricsTextBlock.Text;
        LyricsTabTextBlock.Foreground = LyricsTextBlock.Foreground;
    }

    private static string? FindLyricFile(string audioPath, string folder, string baseName)
    {
        var fileName = Path.GetFileName(audioPath);
        var directCandidates = new[]
        {
            Path.Combine(folder, $"{baseName}.lrc"),
            Path.Combine(folder, $"{baseName}.txt"),
            Path.Combine(folder, $"{fileName}.lrc"),
            Path.Combine(folder, $"{fileName}.txt"),
            Path.Combine(folder, "lyrics.lrc"),
            Path.Combine(folder, "lyrics.txt")
        };

        var direct = directCandidates.FirstOrDefault(File.Exists);
        if (direct is not null)
        {
            return direct;
        }

        try
        {
            var baseKey = ToLooseFileKey(baseName);
            var fileKey = ToLooseFileKey(fileName);
            return Directory.EnumerateFiles(folder)
                .Where(path =>
                    Path.GetExtension(path).Equals(".lrc", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                .Select(path => new
                {
                    Path = path,
                    Key = ToLooseFileKey(Path.GetFileNameWithoutExtension(path))
                })
                .FirstOrDefault(item => item.Key == baseKey || item.Key == fileKey)
                ?.Path;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ToLooseFileKey(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
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
        SyncLyricsTab();
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
        UpdateMiniSpectrum(level);
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
        UpdateMiniSpectrum(0);
    }

    private void UpdateMiniSpectrum(double level)
    {
        var bars = GetMiniSpectrumBars();
        if (bars.Count == 0)
        {
            return;
        }

        for (var i = 0; i < bars.Count; i++)
        {
            var bandPulse = (Math.Sin(_vuPhase * (5.2 + i * 0.31) + i * 0.83) + 1.0) * 0.5;
            var lowBandLift = 1.0 - (i / (double)(bars.Count + 2));
            var shaped = Math.Clamp((level * (0.62 + lowBandLift * 0.42)) + (bandPulse * level * 0.45), 0.0, 1.0);
            bars[i].Height = 4 + (shaped * 22);
            bars[i].Opacity = Math.Clamp(0.18 + shaped * 0.55, 0.18, 0.78);
        }
    }

    private IReadOnlyList<Microsoft.UI.Xaml.Shapes.Rectangle> GetMiniSpectrumBars()
    {
        if (MiniSpectrumBar01 is null)
        {
            return Array.Empty<Microsoft.UI.Xaml.Shapes.Rectangle>();
        }

        return new[]
        {
            MiniSpectrumBar01,
            MiniSpectrumBar02,
            MiniSpectrumBar03,
            MiniSpectrumBar04,
            MiniSpectrumBar05,
            MiniSpectrumBar06,
            MiniSpectrumBar07,
            MiniSpectrumBar08,
            MiniSpectrumBar09,
            MiniSpectrumBar10
        };
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
        if (BackendComboBox?.SelectedItem is ComboBoxItem { Tag: string tag } &&
            Enum.TryParse<AudioBackend>(tag, out var backend))
        {
            return backend;
        }

        return AudioBackend.ZenithWasapi;
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

    private static string SavedLibraryFoldersPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZenithAudio",
        "library.folders.json");

    private async Task LoadSavedLibraryFoldersAsync()
    {
        List<string> savedFolders;
        try
        {
            if (!File.Exists(SavedLibraryFoldersPath))
            {
                return;
            }

            var json = await File.ReadAllTextAsync(SavedLibraryFoldersPath);
            savedFolders = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (Exception)
        {
            return;
        }

        var validFolders = savedFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            .Select(NormalizeFolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validFolders.Count == 0)
        {
            return;
        }

        foreach (var folder in validFolders)
        {
            if (!_libraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                _libraryFolders.Add(folder);
            }
        }

        RefreshLibraryFoldersListView();
        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = $"Cargando biblioteca ({_libraryFolders.Count} carpeta(s))...";

        var allTracks = new List<LibraryTrack>();
        foreach (var folder in _libraryFolders.ToList())
        {
            var folderTracks = await ScanLibraryFolderAsync(folder);
            allTracks.AddRange(folderTracks);
        }

        _libraryTracks.Clear();
        _libraryTracks.AddRange(allTracks);
        _libraryTracks.Sort((left, right) => string.Compare(left.Title, right.Title, StringComparison.CurrentCultureIgnoreCase));

        ShowTrackBrowser("Explorador de biblioteca", _libraryTracks);
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.Message = $"Biblioteca cargada: {_libraryFolders.Count} carpeta(s) | {_libraryTracks.Count} pistas";
    }

    private void SaveLibraryFolders()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavedLibraryFoldersPath)!);
            var json = System.Text.Json.JsonSerializer.Serialize(_libraryFolders, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SavedLibraryFoldersPath, json);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string NormalizeFolderPath(string path)
    {
        var fullPath = Path.GetFullPath(path.Trim());
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private void RefreshLibraryFoldersListView(string? selectedFolder = null)
    {
        LibraryFoldersListView.ItemsSource = null;
        LibraryFoldersListView.ItemsSource = _libraryFolders;
        if (!string.IsNullOrWhiteSpace(selectedFolder))
        {
            LibraryFoldersListView.SelectedItem = _libraryFolders.FirstOrDefault(
                folder => folder.Equals(selectedFolder, StringComparison.OrdinalIgnoreCase));
        }
        else if (_libraryFolders.Count > 0)
        {
            LibraryFoldersListView.SelectedIndex = _libraryFolders.Count - 1;
        }
    }

    private async Task OpenLibraryFolderAsync(string folderPath)
    {
        var normalizedPath = NormalizeFolderPath(folderPath);
        if (!Directory.Exists(normalizedPath))
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = $"La carpeta ya no existe o no es accesible: {normalizedPath}";
            return;
        }

        LibraryFoldersListView.SelectedItem = _libraryFolders.FirstOrDefault(
            folder => folder.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

        var folderTracks = _libraryTracks
            .Where(track => track.Folder.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (folderTracks.Count == 0)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = $"Escaneando carpeta de música: {normalizedPath}";
            folderTracks = await ScanLibraryFolderAsync(normalizedPath);
            _libraryTracks.RemoveAll(track => track.Folder.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
            _libraryTracks.AddRange(folderTracks);
            _libraryTracks.Sort((left, right) => string.Compare(left.Title, right.Title, StringComparison.CurrentCultureIgnoreCase));
        }

        var folderName = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar));
        ShowTrackBrowser(string.IsNullOrWhiteSpace(folderName) ? normalizedPath : folderName, folderTracks);

        if (folderTracks.Count == 0)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = $"No se encontraron archivos de audio compatibles en: {normalizedPath}";
        }
        else
        {
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Message = $"Carpeta abierta: {normalizedPath} | {folderTracks.Count} pistas";
        }
    }

    private async Task AddLibraryFolderAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalizedPath = NormalizeFolderPath(path);
        if (!Directory.Exists(normalizedPath))
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = $"La carpeta seleccionada no existe: {normalizedPath}";
            return;
        }

        var alreadyExists = _libraryFolders.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase);
        if (!alreadyExists)
        {
            _libraryFolders.Add(normalizedPath);
            SaveLibraryFolders();
        }

        RefreshLibraryFoldersListView(normalizedPath);

        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = alreadyExists
            ? $"Actualizando carpeta de música: {normalizedPath}"
            : $"Escaneando carpeta de música: {normalizedPath}";

        var tracks = await ScanLibraryFolderAsync(normalizedPath);
        _libraryTracks.RemoveAll(track => track.Folder.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
        _libraryTracks.AddRange(tracks);
        _libraryTracks.Sort((left, right) => string.Compare(left.Title, right.Title, StringComparison.CurrentCultureIgnoreCase));

        var folderName = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar));
        ShowTrackBrowser(string.IsNullOrWhiteSpace(folderName) ? normalizedPath : folderName, tracks);

        if (tracks.Count == 0)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            StatusInfoBar.Message = $"Carpeta configurada ({normalizedPath}), pero no se encontraron archivos de audio compatibles.";
            return;
        }

        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.Message = alreadyExists
            ? $"Carpeta actualizada: {normalizedPath} | {tracks.Count} pistas indexadas"
            : $"Carpeta agregada: {normalizedPath} | {tracks.Count} pistas indexadas";
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

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = System.IO.FileAttributes.System | System.IO.FileAttributes.ReparsePoint
            };

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(folder, "*.*", options))
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
        _isShowingFolderBrowser = true;
        _visibleLibraryTracks.Clear();
        LibrarySearchBox.Text = string.Empty;
        LibraryBrowserTitleTextBlock.Text = "Carpetas de música";
        LibraryBrowserSubtitleTextBlock.Text = _libraryFolders.Count == 0
            ? "0 carpetas configuradas · Usa 'Agregar carpeta' o 'Música'"
            : $"{_libraryFolders.Count} carpeta(s) configurada(s) · Haz clic en una para abrirla";
        LibraryBrowserListView.ItemsSource = null;
        LibraryBrowserListView.ItemsSource = BuildFolderBrowserItems(_libraryFolders);
    }

    private void ShowTrackBrowser(string title, IEnumerable<LibraryTrack> tracks)
    {
        _isShowingFolderBrowser = false;
        var visibleTracks = tracks.ToList();
        _activeLibraryTitle = title;
        _visibleLibraryTracks.Clear();
        _visibleLibraryTracks.AddRange(visibleTracks);
        LibrarySearchBox.Text = string.Empty;
        LibraryBrowserTitleTextBlock.Text = title;
        LibraryBrowserSubtitleTextBlock.Text = $"{visibleTracks.Count} pistas";
        LibraryBrowserListView.ItemsSource = null;
        LibraryBrowserListView.ItemsSource = BuildTrackBrowserItems(visibleTracks);
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
        var query = LibrarySearchBox.Text?.Trim();
        if (_isShowingFolderBrowser)
        {
            var filteredFolders = string.IsNullOrWhiteSpace(query)
                ? _libraryFolders
                : _libraryFolders
                    .Where(folder => folder.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    .ToList();

            LibraryBrowserTitleTextBlock.Text = "Carpetas de música";
            LibraryBrowserSubtitleTextBlock.Text = string.IsNullOrWhiteSpace(query)
                ? $"{filteredFolders.Count} carpeta(s) configurada(s)"
                : $"{filteredFolders.Count} carpetas encontradas";
            LibraryBrowserListView.ItemsSource = null;
            LibraryBrowserListView.ItemsSource = BuildFolderBrowserItems(filteredFolders);
            return;
        }

        if (_visibleLibraryTracks.Count == 0)
        {
            return;
        }

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
        LibraryBrowserListView.ItemsSource = BuildTrackBrowserItems(filtered);
    }

    private static List<LibraryBrowserItem> BuildFolderBrowserItems(IEnumerable<string> folders)
    {
        return folders
            .OrderBy(folder => folder, StringComparer.CurrentCultureIgnoreCase)
            .Select(folder => LibraryBrowserItem.ForFolder(folder))
            .ToList();
    }

    private static List<LibraryBrowserItem> BuildTrackBrowserItems(IEnumerable<LibraryTrack> tracks)
    {
        var items = new List<LibraryBrowserItem>();
        string? currentHeader = null;
        foreach (var track in tracks.OrderBy(track => GetTrackGroupHeader(track), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(track => track.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var header = GetTrackGroupHeader(track);
            if (!string.Equals(header, currentHeader, StringComparison.CurrentCultureIgnoreCase))
            {
                items.Add(LibraryBrowserItem.ForHeader(header));
                currentHeader = header;
            }

            items.Add(LibraryBrowserItem.ForTrack(track));
        }

        return items;
    }

    private static string GetTrackGroupHeader(LibraryTrack track)
    {
        if (track.Path.StartsWith("cue://", StringComparison.OrdinalIgnoreCase) ||
            track.Path.StartsWith("iso://", StringComparison.OrdinalIgnoreCase))
        {
            return $"— {Path.GetFileNameWithoutExtension(track.Folder)} —";
        }

        var folderName = Path.GetFileName(Path.GetDirectoryName(track.Path) ?? track.Folder);
        if (!string.IsNullOrWhiteSpace(folderName))
        {
            return $"— {folderName} —";
        }

        var first = track.Title.FirstOrDefault(char.IsLetterOrDigit);
        return first == default ? "— # —" : $"— {char.ToUpperInvariant(first)} —";
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
            ApplySignalMetadataForTrack(filePath);
            UpdatePlaybackAvailabilityV2();
            UpdateSignalChain();
            RefreshAntiFakeReport(showStatus: false);
            return;
        }

        if (_isoEntries.TryGetValue(filePath, out var isoEntry))
        {
            TrackTitleTextBlock.Text = isoEntry.Title;
            TrackPathTextBlock.Text = $"{Path.GetFileName(isoEntry.IsoPath)} > {isoEntry.InternalPath}";
            CodecTextBlock.Text = $"ISO {isoEntry.Extension.TrimStart('.').ToUpperInvariant()}";
            TransportBadgeTextBlock.Text = "CARGADO";
            UpdateNowPlayingVisuals(isoEntry.IsoPath);
            ApplySignalMetadataForTrack(filePath);
            UpdatePlaybackAvailabilityV2();
            UpdateSignalChain();
            RefreshAntiFakeReport(showStatus: false);
            return;
        }

        TrackTitleTextBlock.Text = Path.GetFileName(filePath);
        TrackPathTextBlock.Text = filePath;
        CodecTextBlock.Text = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
        TransportBadgeTextBlock.Text = "CARGADO";
        UpdateNowPlayingVisuals(filePath);
        ApplySignalMetadataForTrack(filePath);
        UpdatePlaybackAvailabilityV2();
        UpdateSignalChain();
        RefreshAntiFakeReport(showStatus: false);
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
                track.Extension.Equals(".dff", StringComparison.OrdinalIgnoreCase) ||
                track.Extension.Equals(".alac", StringComparison.OrdinalIgnoreCase) ||
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
        if (_usingAsioEngine)
        {
            _asioEngine.Stop();
            _usingAsioEngine = false;
        }
        else if (_usingWasapiEngine)
        {
            _wasapiEngine.Stop();
            _usingWasapiEngine = false;
        }
        else if (_usingFallbackPlayer)
        {
            _fallbackMediaPlayer.Pause();
            _fallbackMediaPlayer.Source = null;
            _usingFallbackPlayer = false;
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

        ShuffleButton.Opacity = _shuffleEnabled ? 1.0 : 0.72;
        ShuffleButton.Background = new SolidColorBrush(_shuffleEnabled
            ? Microsoft.UI.ColorHelper.FromArgb(255, 7, 39, 62)
            : Microsoft.UI.ColorHelper.FromArgb(255, 6, 18, 31));
        ShuffleButton.BorderBrush = new SolidColorBrush(_shuffleEnabled
            ? Microsoft.UI.ColorHelper.FromArgb(255, 43, 163, 234)
            : Microsoft.UI.ColorHelper.FromArgb(255, 20, 54, 82));

        if (ShuffleIconPath is not null)
        {
            ShuffleIconPath.Stroke = new SolidColorBrush(_shuffleEnabled
                ? Microsoft.UI.ColorHelper.FromArgb(255, 114, 208, 255)
                : Microsoft.UI.ColorHelper.FromArgb(255, 102, 138, 164));
        }
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
            StatusInfoBar.Message = GetDsdPcmFallbackMessage();
            TransportBadgeTextBlock.Text = "DSD -> PCM";
            _vuUsesLiveLevel = true;
            _vuUsesAnalyzer = false;
            var dsdOptions = GetCurrentDsdPlaybackOptions();
            playbackSource = DsfPcmStreamSource.CreateMediaSource(
                filePath,
                _toneSettings,
                level => _vuTargetLevel = MapLiveVuLevel(level),
                dsdOptions.TargetPcmSampleRate > 0 ? dsdOptions.TargetPcmSampleRate : 88200,
                dsdOptions.GainDb);
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
        OutputPathTextBlock.Text = DsdExtensions.Contains(Path.GetExtension(filePath)) ? $"DSD->PCM {GetSelectedOutputLabel()}" : "Fallback de Windows";
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
        _remoteServer?.Dispose();
        CleanupAllTemporaryFallbackFiles();
        _asioEngine.Dispose();
        _wasapiEngine.Dispose();
        _audioEngine.Dispose();
        _audioLevelAnalyzer.Dispose();
    }

    private static bool IsNativeBackendAvailable(AudioBackend backend, string filePath)
    {
        if (backend == AudioBackend.AsioNative)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return AsioPlaybackEngine.GetAvailableDrivers().Count > 0 &&
                (ext is ".dsf" or ".dff" or ".alac" || WindowsFallbackExtensions.Contains(ext));
        }

        if (backend == AudioBackend.ZenithWasapi)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext is ".dsf" or ".dff" or ".alac" || WindowsFallbackExtensions.Contains(ext);
        }

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
            OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn ? "WASAPI Excl. (predet.)" : "WASAPI Compartido";
            ModeTextBlock.Text = ExclusiveModeToggle.IsOn ? "Bit-Perfect" : "WASAPI Comp.";
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = ExclusiveModeToggle.IsOn
                ? "Salida predeterminada en WASAPI Exclusivo. Puedes elegir un DAC o audífono específico en el selector superior."
                : "Salida predeterminada en WASAPI Compartido.";
            return;
        }

        _fallbackMediaPlayer.AudioDevice = selectedDevice.Device;

        OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn
            ? $"{selectedDevice.KindLabel} exclusivo"
            : $"{selectedDevice.KindLabel} compartido";
        ModeTextBlock.Text = ExclusiveModeToggle.IsOn ? "Bit-Perfect" : "WASAPI Comp.";
        StatusInfoBar.Severity = selectedDevice.IsLikelyDac || selectedDevice.IsUsb ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        if (selectedDevice.IsKnownZishanZ2)
        {
            StatusInfoBar.Message = ExclusiveModeToggle.IsOn
                ? "ZiShan Z2 detectado como DAC USB. Zenith usará WASAPI exclusivo (DoP si lo soporta, o DSD->PCM FIR)."
                : "ZiShan Z2 detectado como DAC USB en modo compartido.";
            return;
        }

        StatusInfoBar.Message = ExclusiveModeToggle.IsOn
            ? $"Salida seleccionada: {selectedDevice.Name} (WASAPI Exclusivo)."
            : $"Salida seleccionada: {selectedDevice.Name} (WASAPI Compartido).";
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

    private string GetSelectedOutputLabel()
    {
        var selectedDevice = GetSelectedOutputDevice();
        if (selectedDevice is null || selectedDevice.IsSystemDefault)
        {
            return "Windows";
        }

        return selectedDevice.IsKnownZishanZ2 ? "ZiShan Z2" : selectedDevice.KindLabel;
    }

    private string GetDsdPcmFallbackMessage()
    {
        var selectedDevice = GetSelectedOutputDevice();
        if (selectedDevice?.IsKnownZishanZ2 == true)
        {
            return "ZiShan Z2 detectado. DSD nativo por USB no esta disponible en esta cadena; Zenith convertira DSD a PCM 88.2 kHz y reproducira por el ZiShan.";
        }

        if (selectedDevice is not null && !selectedDevice.IsSystemDefault)
        {
            return $"Salida seleccionada: {selectedDevice.Name}. DSD nativo no esta disponible; Zenith convertira DSD a PCM 88.2 kHz para reproducir por Windows.";
        }

        return "DSD nativo no disponible. Zenith convertira DSD a PCM 88.2 kHz y reproducira por Windows.";
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
            OutputPathTextBlock.Text = canConvertDsd ? $"PCM {GetSelectedOutputLabel()}" : "DLL nativas faltantes";
            StatusInfoBar.Severity = InfoBarSeverity.Warning;
            if (canConvertDsd)
            {
                StatusInfoBar.Message = GetDsdPcmFallbackMessage();
                return;
            }
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
        var dsdOptions = GetCurrentDsdPlaybackOptions();
        var nativeAvailable = hasTrack && IsNativeBackendAvailable(backend, effectivePath);

        BackendTextBlock.Text = GetBackendShortLabel(backend);

        if (!hasTrack)
        {
            PlayButton.IsEnabled = false;
            if (backend == AudioBackend.AsioNative)
            {
                var driverName = GetSelectedAsioDriver()?.Name ?? "Sin driver ASIO";
                ModeTextBlock.Text = "ASIO DSD Nativo";
                OutputPathTextBlock.Text = driverName;
                return;
            }

            ModeTextBlock.Text = ExclusiveModeToggle.IsOn ? "Bit-Perfect" : "WASAPI Comp.";
            OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn ? "WASAPI Exclusivo" : "WASAPI Compartido";
            return;
        }

        if (backend == AudioBackend.AsioNative)
        {
            var driver = GetSelectedAsioDriver();
            var isIso = extension.Equals(".iso", StringComparison.OrdinalIgnoreCase);
            var canPlayAsio = driver is not null && !isIso && (extension is ".dsf" or ".dff" or ".alac" || WindowsFallbackExtensions.Contains(extension));
            PlayButton.IsEnabled = canPlayAsio;
            ModeTextBlock.Text = isDsd ? "ASIO DSD Nativo" : "ASIO Bit-Perfect";
            OutputPathTextBlock.Text = driver?.Name ?? "Sin driver ASIO";
            StatusInfoBar.Severity = canPlayAsio ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            StatusInfoBar.Message = driver is null
                ? "No se detectó un controlador ASIO en Windows. Instala el driver ASIO de tu DAC (ej. FiiO ASIO Driver)."
                : isIso
                    ? "Abre el SACD ISO desde la biblioteca para extraer las pistas DSF sin pérdida."
                    : isDsd
                        ? $"DSD listo para salida ASIO Nativa (1-bit) en {driver.Name}."
                        : $"Pista lista para reproducción ASIO PCM bit-perfect en {driver.Name}.";
            return;
        }

        if (backend == AudioBackend.ZenithWasapi || !nativeAvailable)
        {
            var canPlayIntegrated = CanUseIntegratedWasapi(effectivePath, dsdOptions);
            if (isDsd)
            {
                var isIso = extension.Equals(".iso", StringComparison.OrdinalIgnoreCase);
                PlayButton.IsEnabled = !isIso && canPlayIntegrated;
                ModeTextBlock.Text = dsdOptions.OutputMode == DsdOutputMode.DoPExclusive && ExclusiveModeToggle.IsOn
                    ? "DoP / DSD FIR"
                    : "DSD -> PCM FIR";
                OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn
                    ? $"WASAPI Excl. {GetSelectedOutputLabel()}"
                    : $"WASAPI Comp. {GetSelectedOutputLabel()}";
                StatusInfoBar.Severity = PlayButton.IsEnabled ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
                StatusInfoBar.Message = isIso
                    ? "Abre el SACD ISO desde la biblioteca para extraer las pistas DSF sin pérdida."
                    : PlayButton.IsEnabled
                        ? $"DSD listo en Zenith WASAPI ({(dsdOptions.OutputMode == DsdOutputMode.DoPExclusive && ExclusiveModeToggle.IsOn ? "DoP exclusivo / respaldo PCM FIR" : "PCM FIR fase lineal")})."
                        : "Modo 'Solo DSD nativo (BASS / MPV)' activo y faltan DLL externas. Cambia a DSD a PCM FIR.";
                return;
            }

            PlayButton.IsEnabled = canPlayIntegrated || WindowsFallbackExtensions.Contains(extension);
            ModeTextBlock.Text = ExclusiveModeToggle.IsOn ? "Bit-Perfect" : "WASAPI Comp.";
            OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn
                ? $"WASAPI Excl. {GetSelectedOutputLabel()}"
                : $"WASAPI Comp. {GetSelectedOutputLabel()}";
            StatusInfoBar.Severity = PlayButton.IsEnabled ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            StatusInfoBar.Message = PlayButton.IsEnabled
                ? $"Pista lista en Zenith WASAPI ({(ExclusiveModeToggle.IsOn ? "Modo Exclusivo" : "Modo Compartido")})."
                : "Este formato requiere las librerías opcionales de BASS o MPV.";
            return;
        }

        PlayButton.IsEnabled = true;

        if (backend == AudioBackend.MpvWasapi && !IsExclusiveDacSelected())
        {
            ModeTextBlock.Text = "MPV Comp.";
            OutputPathTextBlock.Text = "MPV compartido";
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = "MPV disponible en salida compartida.";
            return;
        }

        ModeTextBlock.Text = ExclusiveModeToggle.IsOn ? "Bit-Perfect" : "Compartido";
        OutputPathTextBlock.Text = ExclusiveModeToggle.IsOn ? "DAC exclusivo" : "Windows compartido";
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.Message = "Pista lista para reproducir";
    }

    private void RefreshBackendLabels()
    {
        if (BackendComboBox is null || BackendComboBox.Items.Count < 4)
        {
            return;
        }

        if (BackendComboBox.Items[0] is ComboBoxItem zenithItem)
        {
            zenithItem.Content = "Zenith WASAPI";
        }

        if (BackendComboBox.Items[1] is ComboBoxItem asioItem)
        {
            asioItem.Content = _asioDrivers.Count > 0
                ? "ASIO (DSD Nativo)"
                : "ASIO (sin drivers)";
        }

        if (BackendComboBox.Items[2] is ComboBoxItem bassItem)
        {
            bassItem.Content = IsDllAvailable("bass.dll") && IsDllAvailable("basswasapi.dll")
                ? "BASS WASAPI"
                : "BASS WASAPI (opcional)";
        }

        if (BackendComboBox.Items[3] is ComboBoxItem mpvItem)
        {
            mpvItem.Content = IsDllAvailable("mpv-2.dll")
                ? "MPV WASAPI"
                : "MPV WASAPI (opcional)";
        }
    }

    private static string SavedAudioSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZenithAudio",
        "audio.settings.json");

    private void LoadSavedAudioSettings()
    {
        _isLoadingAudioSettings = true;
        try
        {
            if (!File.Exists(SavedAudioSettingsPath))
            {
                return;
            }

            var json = File.ReadAllText(SavedAudioSettingsPath);
            var settings = System.Text.Json.JsonSerializer.Deserialize<SavedAudioSettings>(json);
            if (settings is null)
            {
                return;
            }

            _savedAudioSettings = settings;

            if (BackendComboBox is not null && settings.BackendIndex >= 0 && settings.BackendIndex < BackendComboBox.Items.Count)
            {
                BackendComboBox.SelectedIndex = settings.BackendIndex;
            }

            if (ExclusiveModeToggle is not null)
            {
                SetExclusiveToggleSilently(false);
            }

            if (DsdModeComboBox is not null && settings.DsdModeIndex >= 0 && settings.DsdModeIndex < DsdModeComboBox.Items.Count)
            {
                DsdModeComboBox.SelectedIndex = settings.DsdModeIndex;
            }

            if (DsdPcmRateComboBox is not null && settings.DsdPcmRateIndex >= 0 && settings.DsdPcmRateIndex < DsdPcmRateComboBox.Items.Count)
            {
                DsdPcmRateComboBox.SelectedIndex = settings.DsdPcmRateIndex;
            }

            if (DsdGainComboBox is not null && settings.DsdGainIndex >= 0 && settings.DsdGainIndex < DsdGainComboBox.Items.Count)
            {
                DsdGainComboBox.SelectedIndex = settings.DsdGainIndex;
            }

            if (BufferSlider is not null && settings.BufferMilliseconds is >= 50 and <= 500)
            {
                BufferSlider.Value = settings.BufferMilliseconds;
            }
        }
        catch
        {
        }
        finally
        {
            _isLoadingAudioSettings = false;
        }
    }

    private void SaveAudioSettings()
    {
        if (_isLoadingAudioSettings)
        {
            return;
        }

        try
        {
            var settings = new SavedAudioSettings(
                GetSelectedOutputDevice()?.Id,
                BackendComboBox?.SelectedIndex ?? 0,
                ExclusiveModeToggle?.IsOn ?? false,
                DsdModeComboBox?.SelectedIndex ?? 0,
                DsdPcmRateComboBox?.SelectedIndex ?? 0,
                DsdGainComboBox?.SelectedIndex ?? 2,
                (int)(BufferSlider?.Value ?? 100),
                GetSelectedAsioDriver()?.Name);

            _savedAudioSettings = settings;
            Directory.CreateDirectory(Path.GetDirectoryName(SavedAudioSettingsPath)!);
            File.WriteAllText(
                SavedAudioSettingsPath,
                System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private sealed record SavedAudioSettings(
        string? DeviceId,
        int BackendIndex,
        bool ExclusiveMode,
        int DsdModeIndex,
        int DsdPcmRateIndex,
        int DsdGainIndex,
        int BufferMilliseconds,
        string? AsioDriverName = null);

    private bool ShouldConvertDsdToPcm()
    {
        return DsdModeComboBox?.SelectedIndex == 3;
    }

    private void RenderZenithAiTranscript(
        StackPanel transcriptPanel,
        ScrollViewer scrollViewer,
        double dialogWidth,
        string? pendingMessage = null)
    {
        transcriptPanel.Children.Clear();

        if (_zenithAiMessages.Count == 0 && string.IsNullOrWhiteSpace(pendingMessage))
        {
            AddZenithAiBubble(
                transcriptPanel,
                "assistant",
                "ZenithAI listo. Pregúntame por la pista actual, diferencias entre DSD/FLAC/PCM, historia de un álbum, configuración de Windows, DACs o escucha crítica.",
                dialogWidth);
            ScrollZenithAiTranscriptToEnd(scrollViewer);
            return;
        }

        foreach (var message in _zenithAiMessages)
        {
            AddZenithAiBubble(transcriptPanel, message.Role, message.Content, dialogWidth);
        }

        if (!string.IsNullOrWhiteSpace(pendingMessage))
        {
            AddZenithAiBubble(transcriptPanel, "assistant", pendingMessage, dialogWidth, isPending: true);
        }

        ScrollZenithAiTranscriptToEnd(scrollViewer);
    }

    private static void AddZenithAiBubble(
        StackPanel transcriptPanel,
        string role,
        string content,
        double dialogWidth,
        bool isPending = false)
    {
        var isAssistant = role.Equals("assistant", StringComparison.OrdinalIgnoreCase);
        var maxBubbleWidth = Math.Max(260, dialogWidth * 0.78);

        var label = new TextBlock
        {
            Text = isAssistant ? "ZenithAI" : "Tú",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(isAssistant
                ? Microsoft.UI.ColorHelper.FromArgb(255, 146, 213, 255)
                : Microsoft.UI.ColorHelper.FromArgb(255, 225, 232, 240))
        };

        var messageText = new TextBlock
        {
            Text = NormalizeZenithAiText(content),
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true,
            FontSize = 14,
            LineHeight = 21,
            Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 245, 247, 250)),
            MaxWidth = maxBubbleWidth
        };

        var stack = new StackPanel
        {
            Spacing = 6
        };
        stack.Children.Add(label);
        stack.Children.Add(messageText);

        var bubble = new Border
        {
            MaxWidth = maxBubbleWidth + 28,
            Padding = new Thickness(14, 11, 14, 12),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = isAssistant ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Background = new SolidColorBrush(isAssistant
                ? Microsoft.UI.ColorHelper.FromArgb(255, 18, 28, 42)
                : Microsoft.UI.ColorHelper.FromArgb(255, 8, 77, 112)),
            BorderBrush = new SolidColorBrush(isAssistant
                ? Microsoft.UI.ColorHelper.FromArgb(160, 95, 191, 255)
                : Microsoft.UI.ColorHelper.FromArgb(190, 38, 173, 228)),
            Opacity = isPending ? 0.78 : 1,
            Child = stack
        };

        transcriptPanel.Children.Add(bubble);
    }

    private static string NormalizeZenithAiText(string value)
    {
        return value
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("### ", string.Empty, StringComparison.Ordinal)
            .Replace("## ", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private void ScrollZenithAiTranscriptToEnd(ScrollViewer scrollViewer)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            scrollViewer.UpdateLayout();
            scrollViewer.ChangeView(null, scrollViewer.ScrollableHeight, null, disableAnimation: false);
        });
    }

    private string BuildZenithAiAudioContext(string? userQuestion = null)
    {
        var selectedOutput = GetSelectedOutputDevice()?.ToString() ?? "Salida predeterminada de Windows";
        var currentTrack = string.IsNullOrWhiteSpace(_currentFilePath)
            ? "Sin pista cargada"
            : $"{TrackTitleTextBlock.Text} ({_currentFilePath})";
        var availableOutputs = string.Join(", ", _outputDevices.Select(d => d.Name).Take(8));
        var availableAsio = _asioDrivers.Count > 0 ? string.Join(", ", _asioDrivers.Select(d => d.Name)) : "ninguno";

        var libraryMatches = string.Empty;
        if (_libraryTracks.Count > 0)
        {
            var tokens = string.IsNullOrWhiteSpace(userQuestion)
                ? Array.Empty<string>()
                : NormalizeCommandText(userQuestion)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length >= 3)
                    .ToArray();

            var matched = tokens.Length > 0
                ? _libraryTracks
                    .Where(t => tokens.Any(tok =>
                        NormalizeCommandText(t.Title).Contains(tok, StringComparison.Ordinal) ||
                        NormalizeCommandText(Path.GetFileName(Path.GetDirectoryName(t.Path) ?? t.Folder)).Contains(tok, StringComparison.Ordinal) ||
                        t.Format.Contains(tok, StringComparison.OrdinalIgnoreCase)))
                    .Take(15)
                    .Select(t => $"{t.Title} [{t.Format}]")
                    .ToList()
                : [];

            if (matched.Count == 0)
            {
                matched = _libraryTracks.Take(10).Select(t => $"{t.Title} [{t.Format}]").ToList();
            }

            libraryMatches = $" Biblioteca cargada: {_libraryTracks.Count} pistas (muestra relevante: {string.Join("; ", matched)}).";
        }

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
            $"Dispositivo actual: {selectedOutput}. " +
            $"Salidas disponibles: {availableOutputs}. " +
            $"Drivers ASIO instalados: {availableAsio}. " +
            $"Volumen: {(int)(VolumeSlider?.Value ?? 100)}%. " +
            $"Aleatorio: {(_shuffleEnabled ? "ON" : "OFF")}. " +
            $"Cadena de señal: fuente {SignalSourceTextBlock.Text}, decodificación {SignalDecodeTextBlock.Text}, DSP {SignalDspTextBlock.Text}, salida {SignalOutputTextBlock.Text}. " +
            $"Control de tono: {ToneSummaryTextBlock.Text}." +
            libraryMatches;
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
            : IsKnownZishanZ2 ? "ZiShan Z2 DAC"
            : IsLikelyDac ? "DAC"
            : IsUsb ? "USB"
            : "WASAPI";

        public bool IsKnownZishanZ2 => IsZishanZ2Endpoint(Name, Id);

        public static OutputDeviceOption FromDevice(DeviceInformation device)
        {
            var name = string.IsNullOrWhiteSpace(device.Name) ? "Dispositivo de salida de audio" : device.Name;
            var propertyText = GetDevicePropertyText(device);
            var normalizedName = name.ToLowerInvariant();
            var normalizedId = $"{device.Id} {propertyText}".ToLowerInvariant();
            var isKnownZishanZ2 = IsZishanZ2Endpoint(name, normalizedId);
            var isUsb = isKnownZishanZ2 ||
                normalizedName.Contains("usb", StringComparison.Ordinal) ||
                normalizedId.Contains("usb", StringComparison.Ordinal);
            var isOnboardOrGeneric =
                normalizedName.Contains("realtek", StringComparison.Ordinal) ||
                normalizedName.Contains("high definition audio", StringComparison.Ordinal) ||
                normalizedName.Contains("conexant", StringComparison.Ordinal) ||
                normalizedName.Contains("idt", StringComparison.Ordinal) ||
                normalizedName.Contains("intel", StringComparison.Ordinal) ||
                normalizedName.Contains("nvidia", StringComparison.Ordinal) ||
                normalizedName.Contains("amd high definition", StringComparison.Ordinal) ||
                normalizedName.Contains("bluetooth", StringComparison.Ordinal) ||
                normalizedName.Contains("hands-free", StringComparison.Ordinal) ||
                normalizedName.Contains("gaming headset", StringComparison.Ordinal) ||
                normalizedName.Contains("redragon", StringComparison.Ordinal) ||
                normalizedName.Contains("hyperx", StringComparison.Ordinal) ||
                normalizedName.Contains("logitech", StringComparison.Ordinal) ||
                normalizedName.Contains("razer", StringComparison.Ordinal) ||
                normalizedName.Contains("corsair", StringComparison.Ordinal) ||
                normalizedName.Contains("steelseries", StringComparison.Ordinal);

            var driverInfo = WasapiPlaybackEngine.InspectEndpointDriver(device.Id, name);
            var isLikelyDac =
                !isOnboardOrGeneric && (
                    driverInfo.HasDedicatedDacDriver ||
                    isKnownZishanZ2 ||
                    normalizedName.Contains("fiio", StringComparison.Ordinal) ||
                    normalizedName.Contains("jadeaudio", StringComparison.Ordinal) ||
                    normalizedName.Contains("jade audio", StringComparison.Ordinal) ||
                    normalizedName.Contains("ja11", StringComparison.Ordinal) ||
                    normalizedName.Contains("ka11", StringComparison.Ordinal) ||
                    normalizedName.Contains("ka13", StringComparison.Ordinal) ||
                    normalizedName.Contains("ka15", StringComparison.Ordinal) ||
                    normalizedName.Contains("ka17", StringComparison.Ordinal) ||
                    normalizedName.Contains("dac", StringComparison.Ordinal) ||
                    normalizedName.Contains("asio", StringComparison.Ordinal) ||
                    normalizedName.Contains("hifi", StringComparison.Ordinal) ||
                    normalizedName.Contains("hi-fi", StringComparison.Ordinal) ||
                    normalizedName.Contains("ifi", StringComparison.Ordinal) ||
                    normalizedName.Contains("topping", StringComparison.Ordinal) ||
                    normalizedName.Contains("smsl", StringComparison.Ordinal) ||
                    normalizedName.Contains("s.m.s.l", StringComparison.Ordinal) ||
                    normalizedName.Contains("schiit", StringComparison.Ordinal) ||
                    normalizedName.Contains("chord", StringComparison.Ordinal) ||
                    normalizedName.Contains("focusrite", StringComparison.Ordinal) ||
                    normalizedName.Contains("scarlett", StringComparison.Ordinal) ||
                    normalizedName.Contains("motu", StringComparison.Ordinal) ||
                    normalizedName.Contains("audient", StringComparison.Ordinal) ||
                    normalizedName.Contains("rme", StringComparison.Ordinal) ||
                    normalizedName.Contains("steinberg", StringComparison.Ordinal) ||
                    normalizedName.Contains("dragonfly", StringComparison.Ordinal) ||
                    normalizedName.Contains("audioquest", StringComparison.Ordinal) ||
                    normalizedName.Contains("shanling", StringComparison.Ordinal) ||
                    normalizedName.Contains("hiby", StringComparison.Ordinal) ||
                    normalizedName.Contains("cayin", StringComparison.Ordinal) ||
                    normalizedName.Contains("questyle", StringComparison.Ordinal) ||
                    normalizedName.Contains("moondrop", StringComparison.Ordinal) ||
                    normalizedName.Contains("tempotec", StringComparison.Ordinal) ||
                    normalizedName.Contains("qudelix", StringComparison.Ordinal) ||
                    normalizedName.Contains("sabaj", StringComparison.Ordinal) ||
                    normalizedName.Contains("loxjie", StringComparison.Ordinal) ||
                    normalizedName.Contains("gustard", StringComparison.Ordinal) ||
                    normalizedName.Contains("denafrips", StringComparison.Ordinal) ||
                    normalizedName.Contains("aune", StringComparison.Ordinal) ||
                    normalizedName.Contains("xduoo", StringComparison.Ordinal) ||
                    normalizedName.Contains("jcally", StringComparison.Ordinal) ||
                    normalizedName.Contains("hidizs", StringComparison.Ordinal) ||
                    normalizedName.Contains("ibasso", StringComparison.Ordinal) ||
                    normalizedName.Contains("xmos", StringComparison.Ordinal) ||
                    normalizedName.Contains("savitech", StringComparison.Ordinal) ||
                    normalizedName.Contains("bravo-hd", StringComparison.Ordinal) ||
                    normalizedName.Contains("usb audio 2.0", StringComparison.Ordinal) ||
                    normalizedName.Contains("usb audio device", StringComparison.Ordinal) ||
                    normalizedId.Contains("fiio", StringComparison.Ordinal) ||
                    normalizedId.Contains("tusbaudio", StringComparison.Ordinal) ||
                    normalizedId.Contains("vid_2972", StringComparison.Ordinal) ||
                    normalizedId.Contains("xmos", StringComparison.Ordinal));

            return new OutputDeviceOption(name, device.Id, device, false, isUsb, isLikelyDac);
        }

        private static string GetDevicePropertyText(DeviceInformation device)
        {
            return string.Join(
                " ",
                device.Properties.Values.Select(value => value switch
                {
                    null => string.Empty,
                    string[] values => string.Join(" ", values),
                    _ => value.ToString() ?? string.Empty
                }));
        }

        private static bool IsZishanZ2Endpoint(string name, string id)
        {
            var combined = $"{name} {id}".ToLowerInvariant();
            return combined.Contains("zishan", StringComparison.Ordinal) ||
                combined.Contains("vid_0483&pid_5730", StringComparison.Ordinal) ||
                combined.Contains("23db11b9-b60c-462d-9496-47342b9e6ec1", StringComparison.Ordinal) ||
                (combined.Contains("z2", StringComparison.Ordinal) &&
                    combined.Contains("hifi", StringComparison.Ordinal) &&
                    combined.Contains("player", StringComparison.Ordinal));
        }

        public override string ToString()
        {
            return IsSystemDefault ? Name : $"{Name}  |  {KindLabel}";
        }
    }

    public sealed record LibraryBrowserItem(
        string HeaderText,
        Visibility HeaderVisibility,
        Visibility TrackVisibility,
        string DisplayTitle,
        string SubTitle,
        string BadgeText,
        Brush BadgeBackground,
        Brush BadgeBorderBrush,
        Brush BadgeForeground,
        bool IsHeader,
        LibraryTrack? Track,
        string? FolderPath)
    {
        public static LibraryBrowserItem ForHeader(string header) => new(
            header,
            Visibility.Visible,
            Visibility.Collapsed,
            string.Empty,
            string.Empty,
            string.Empty,
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0, 0, 0, 0)),
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0, 0, 0, 0)),
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 124, 158, 188)),
            true,
            null,
            null);

        public static LibraryBrowserItem ForFolder(string folderPath)
        {
            var title = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(title))
            {
                title = folderPath;
            }

            return new LibraryBrowserItem(
                string.Empty,
                Visibility.Collapsed,
                Visibility.Visible,
                title,
                folderPath,
                "DIR",
                new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(78, 20, 42, 62)),
                new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(118, 64, 122, 168)),
                new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 210, 230, 244)),
                false,
                null,
                folderPath);
        }

        public static LibraryBrowserItem ForTrack(LibraryTrack track)
        {
            var badge = GetBadgeStyle(track.Extension);
            var album = Path.GetFileName(Path.GetDirectoryName(track.Path) ?? track.Folder);
            return new LibraryBrowserItem(
                string.Empty,
                Visibility.Collapsed,
                Visibility.Visible,
                track.Title,
                string.IsNullOrWhiteSpace(album) ? track.Format : album,
                badge.Text,
                badge.Background,
                badge.Border,
                badge.Foreground,
                false,
                track,
                null);
        }

        private static (string Text, Brush Background, Brush Border, Brush Foreground) GetBadgeStyle(string extension)
        {
            var normalized = extension.ToLowerInvariant();
            var text = normalized.TrimStart('.').ToUpperInvariant();

            if (normalized is ".dsf" or ".dff" or ".iso")
            {
                return (
                    text,
                    new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(82, 20, 58, 142)),
                    new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(150, 82, 150, 255)),
                    new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 205, 226, 255)));
            }

            if (normalized is ".flac" or ".wav" or ".aiff" or ".aif" or ".alac" or ".mqa" or ".ape" or ".wv")
            {
                return (
                    text,
                    new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(78, 8, 74, 116)),
                    new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(152, 64, 190, 255)),
                    new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 198, 239, 255)));
            }

            if (normalized is ".mp3" or ".aac" or ".m4a" or ".ogg" or ".opus")
            {
                return (
                    text,
                    new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(76, 28, 42, 58)),
                    new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(124, 92, 116, 144)),
                    new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 206, 216, 226)));
            }

            return (
                text,
                new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(68, 24, 48, 70)),
                new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(112, 82, 132, 170)),
                new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 216, 230, 242)));
        }
    }

    public sealed record LibraryTrack(string Title, string Format, string Extension, string Path, string Folder)
    {
        public override string ToString()
        {
            return $"{Title}  |  {Format}";
        }
    }

    private sealed record ZenithAiToneProfile(
        string Name,
        string Description,
        bool EqEnabled,
        bool DspBypassed,
        double PreampDb,
        double SubBassDb,
        double PresenceDb,
        double AirDb);

    private sealed record SyncedLyricLine(TimeSpan Time, string Text);
}


