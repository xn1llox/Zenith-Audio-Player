using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using ZenithAudio.Core.Audio;
using ZenithAudio.Core.Remote;

namespace ZenithAudio;

public sealed partial class MainWindow
{
    private ZenithRemoteSettings _remoteSettings = new();
    private ZenithRemoteServer? _remoteServer;
    private readonly object _coverCacheLock = new();
    private byte[]? _currentCoverBytes;
    private string _currentCoverMimeType = "image/jpeg";
    private int _currentCoverVersion;

    private void InitializeRemoteControl()
    {
        _remoteSettings = ZenithRemoteSettings.Load();
        _remoteServer = new ZenithRemoteServer(new RemoteBridgeAdapter(this));
        _remoteServer.ClientsChanged += () =>
        {
            DispatcherQueue.TryEnqueue(UpdateRemoteClientsText);
        };

        if (RemotePortTextBox is not null)
        {
            RemotePortTextBox.Text = _remoteSettings.Port.ToString(CultureInfo.InvariantCulture);
        }

        if (RemotePinBox is not null)
        {
            RemotePinBox.Password = _remoteSettings.Pin;
        }

        if (RemoteAllowLanToggle is not null)
        {
            RemoteAllowLanToggle.IsOn = _remoteSettings.AllowLan;
        }

        if (RemoteAutoStartToggle is not null)
        {
            RemoteAutoStartToggle.IsOn = _remoteSettings.AutoStart;
        }

        UpdateRemoteControlVisuals();

        if (_remoteSettings.AutoStart)
        {
            TryStartRemoteServer(showBanner: false);
        }
    }

    private void UpdateCachedCoverBytes(byte[]? bytes, string? mimeType)
    {
        lock (_coverCacheLock)
        {
            _currentCoverBytes = bytes;
            _currentCoverMimeType = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType;
            _currentCoverVersion++;
        }
    }

    private ZenithRemoteCoverData? GetCachedCoverData()
    {
        lock (_coverCacheLock)
        {
            if (_currentCoverBytes is null || _currentCoverBytes.Length == 0)
            {
                return null;
            }

            return new ZenithRemoteCoverData(_currentCoverBytes, _currentCoverMimeType);
        }
    }

    private int GetCurrentCoverVersion()
    {
        lock (_coverCacheLock)
        {
            return _currentCoverVersion;
        }
    }

    private bool HasCachedCover()
    {
        lock (_coverCacheLock)
        {
            return _currentCoverBytes is { Length: > 0 };
        }
    }

    private void RemoteControlButton_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteControlOverlayGrid is null)
        {
            return;
        }

        var opening = RemoteControlOverlayGrid.Visibility != Visibility.Visible;
        if (opening && ZenithAiOverlayGrid is not null)
        {
            ZenithAiOverlayGrid.Visibility = Visibility.Collapsed;
        }

        RemoteControlOverlayGrid.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        if (opening)
        {
            UpdateRemoteControlVisuals();
        }
    }

    private void CloseRemoteControlButton_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteControlOverlayGrid is not null)
        {
            RemoteControlOverlayGrid.Visibility = Visibility.Collapsed;
        }
    }

    private void RemoteControlOverlayGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (RemoteControlOverlayGrid is not null)
        {
            RemoteControlOverlayGrid.Visibility = Visibility.Collapsed;
        }
    }

    private void RemoteControlPanelBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void RemoteToggleServerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_remoteServer?.IsRunning == true)
        {
            StopRemoteServer(showBanner: true);
        }
        else
        {
            ApplyRemoteSettingsFromInputs();
            TryStartRemoteServer(showBanner: true);
        }
    }

    private void RemoteSaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var wasRunning = _remoteServer?.IsRunning == true;
        if (!ApplyRemoteSettingsFromInputs())
        {
            return;
        }

        if (wasRunning)
        {
            TryStartRemoteServer(showBanner: true);
        }
        else
        {
            UpdateRemoteControlVisuals();
            if (RemoteStatusDetailTextBlock is not null)
            {
                RemoteStatusDetailTextBlock.Text = $"Configuración guardada (puerto {_remoteSettings.Port}). Pulsa Iniciar para activarlo.";
            }
        }
    }

    private void RemoteCopyUrlButton_Click(object sender, RoutedEventArgs e)
    {
        var url = _remoteServer?.PrimaryUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(url);
            Clipboard.SetContent(package);

            if (RemoteStatusDetailTextBlock is not null)
            {
                RemoteStatusDetailTextBlock.Text = $"URL copiada al portapapeles: {url}";
            }

            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Message = $"URL remota copiada: {url}";
        }
        catch (Exception ex)
        {
            if (RemoteStatusDetailTextBlock is not null)
            {
                RemoteStatusDetailTextBlock.Text = $"No se pudo copiar la URL: {ex.Message}";
            }
        }
    }

    private void RemoteOpenBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        if (_remoteServer is null || !_remoteServer.IsRunning)
        {
            return;
        }

        var localUrl = $"http://localhost:{_remoteServer.ActivePort}";
        try
        {
            Process.Start(new ProcessStartInfo(localUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (RemoteStatusDetailTextBlock is not null)
            {
                RemoteStatusDetailTextBlock.Text = $"No se pudo abrir el navegador: {ex.Message}";
            }
        }
    }

    private bool ApplyRemoteSettingsFromInputs()
    {
        var rawPort = RemotePortTextBox?.Text?.Trim() ?? string.Empty;
        if (!int.TryParse(rawPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port < 1024 || port > 65535)
        {
            if (RemoteStatusDetailTextBlock is not null)
            {
                RemoteStatusDetailTextBlock.Text = "Puerto inválido. Usa un número entre 1024 y 65535 (ej. 8787).";
            }

            return false;
        }

        _remoteSettings.Port = port;
        _remoteSettings.Pin = RemotePinBox?.Password?.Trim() ?? string.Empty;
        _remoteSettings.AllowLan = RemoteAllowLanToggle?.IsOn ?? true;
        _remoteSettings.AutoStart = RemoteAutoStartToggle?.IsOn ?? false;
        _remoteSettings.Save();
        return true;
    }

    private void TryStartRemoteServer(bool showBanner)
    {
        if (_remoteServer is null)
        {
            return;
        }

        try
        {
            _remoteServer.Start(_remoteSettings);
            UpdateRemoteControlVisuals();

            if (RemoteStatusDetailTextBlock is not null)
            {
                RemoteStatusDetailTextBlock.Text = _remoteSettings.AllowLan
                    ? "Servidor activo en red local. Abre la URL de arriba en tu teléfono (mismo Wi-Fi). Si Windows Firewall pregunta, permite Red Privada."
                    : "Servidor activo solo en este equipo (localhost). Activa Red Local si deseas entrar desde tu teléfono.";
            }

            if (showBanner)
            {
                StatusInfoBar.Severity = InfoBarSeverity.Success;
                StatusInfoBar.Message = $"Control Remoto activo en {_remoteServer.PrimaryUrl}";
            }
        }
        catch (Exception ex)
        {
            UpdateRemoteControlVisuals();
            if (RemoteStatusDetailTextBlock is not null)
            {
                RemoteStatusDetailTextBlock.Text = $"No se pudo iniciar en el puerto {_remoteSettings.Port}: {ex.Message}";
            }

            if (showBanner)
            {
                StatusInfoBar.Severity = InfoBarSeverity.Error;
                StatusInfoBar.Message = $"Error al iniciar Control Remoto: {ex.Message}";
            }
        }
    }

    private void StopRemoteServer(bool showBanner)
    {
        _remoteServer?.Stop();
        UpdateRemoteControlVisuals();

        if (RemoteStatusDetailTextBlock is not null)
        {
            RemoteStatusDetailTextBlock.Text = "Servidor detenido. Ningún dispositivo externo puede conectarse.";
        }

        if (showBanner)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = "Control Remoto LAN detenido.";
        }
    }

    private void UpdateRemoteControlVisuals()
    {
        var running = _remoteServer?.IsRunning == true;

        if (RemoteStatusDot is not null)
        {
            RemoteStatusDot.Fill = new SolidColorBrush(running
                ? Microsoft.UI.ColorHelper.FromArgb(255, 56, 227, 165)
                : Microsoft.UI.ColorHelper.FromArgb(255, 78, 116, 143));
        }

        if (RemoteControlButton is not null)
        {
            RemoteControlButton.Background = new SolidColorBrush(running
                ? Microsoft.UI.ColorHelper.FromArgb(255, 7, 45, 52)
                : Microsoft.UI.ColorHelper.FromArgb(255, 7, 19, 32));
            RemoteControlButton.BorderBrush = new SolidColorBrush(running
                ? Microsoft.UI.ColorHelper.FromArgb(255, 56, 227, 165)
                : Microsoft.UI.ColorHelper.FromArgb(255, 23, 61, 91));
        }

        if (RemoteStateBadgeBorder is not null && RemoteStateBadgeTextBlock is not null)
        {
            RemoteStateBadgeTextBlock.Text = running
                ? (_remoteServer!.ActiveAllowLan ? "ACTIVO · LAN" : "ACTIVO · LOCAL")
                : "DETENIDO";
            RemoteStateBadgeTextBlock.Foreground = new SolidColorBrush(running
                ? Microsoft.UI.ColorHelper.FromArgb(255, 134, 247, 206)
                : Microsoft.UI.ColorHelper.FromArgb(255, 140, 179, 204));
            RemoteStateBadgeBorder.Background = new SolidColorBrush(running
                ? Microsoft.UI.ColorHelper.FromArgb(255, 6, 46, 39)
                : Microsoft.UI.ColorHelper.FromArgb(255, 7, 23, 38));
            RemoteStateBadgeBorder.BorderBrush = new SolidColorBrush(running
                ? Microsoft.UI.ColorHelper.FromArgb(255, 44, 189, 139)
                : Microsoft.UI.ColorHelper.FromArgb(255, 27, 71, 102));
        }

        if (RemoteToggleServerButton is not null && RemoteToggleServerTextBlock is not null && RemoteToggleServerIcon is not null)
        {
            if (running)
            {
                RemoteToggleServerTextBlock.Text = "Detener servidor remoto";
                RemoteToggleServerIcon.Glyph = "\uE71A";
                RemoteToggleServerIcon.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 255, 168, 178));
                RemoteToggleServerButton.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 64, 18, 28));
                RemoteToggleServerButton.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 214, 84, 102));
            }
            else
            {
                RemoteToggleServerTextBlock.Text = "Iniciar servidor remoto";
                RemoteToggleServerIcon.Glyph = "\uE768";
                RemoteToggleServerIcon.Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 127, 224, 255));
                RemoteToggleServerButton.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 10, 76, 120));
                RemoteToggleServerButton.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 56, 182, 255));
            }
        }

        if (RemotePrimaryUrlTextBox is not null)
        {
            RemotePrimaryUrlTextBox.Text = running ? _remoteServer!.PrimaryUrl : "Servidor detenido";
        }

        if (RemoteCopyUrlButton is not null)
        {
            RemoteCopyUrlButton.IsEnabled = running;
        }

        if (RemoteOpenBrowserButton is not null)
        {
            RemoteOpenBrowserButton.IsEnabled = running;
        }

        if (RemoteExtraUrlsTextBlock is not null)
        {
            if (running && _remoteServer!.AccessibleUrls.Count > 0)
            {
                var extras = _remoteServer.AccessibleUrls.Skip(1).ToList();
                RemoteExtraUrlsTextBlock.Text = extras.Count > 0
                    ? "Otras direcciones: " + string.Join("  ·  ", extras)
                    : $"Localhost listo en http://localhost:{_remoteServer.ActivePort}";
            }
            else
            {
                RemoteExtraUrlsTextBlock.Text = $"Inicia el servidor para generar tu enlace local http://IP:{_remoteSettings.Port}";
            }
        }

        UpdateRemoteClientsText();
    }

    private void UpdateRemoteClientsText()
    {
        if (RemoteClientsStatusTextBlock is null)
        {
            return;
        }

        if (_remoteServer is null || !_remoteServer.IsRunning)
        {
            RemoteClientsStatusTextBlock.Text = "Dispositivos activos: 0";
            return;
        }

        var ips = _remoteServer.GetActiveClientIps();
        RemoteClientsStatusTextBlock.Text = ips.Count == 0
            ? "Dispositivos activos: 0 (esperando conexión)"
            : $"Dispositivos activos: {ips.Count} ({string.Join(", ", ips.Take(3))})";
    }

    private Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var result = await action();
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
        {
            tcs.TrySetException(new InvalidOperationException("La ventana principal de Zenith Audio ya no está disponible."));
        }

        return tcs.Task;
    }

    private ZenithRemoteState BuildRemoteStateSnapshot()
    {
        TimeSpan position = TimeSpan.Zero;
        TimeSpan duration = TimeSpan.Zero;

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
        else if (PlaybackProgressSlider is not null)
        {
            position = TimeSpan.FromSeconds(Math.Max(0, PlaybackProgressSlider.Value));
            duration = TimeSpan.FromSeconds(Math.Max(0, PlaybackProgressSlider.Maximum));
        }

        var isPlaying = StopButton?.IsEnabled == true && !_isPaused;
        var selectedBackendIndex = BackendComboBox?.SelectedIndex ?? 0;
        var selectedOutputIndex = OutputDeviceComboBox?.SelectedIndex ?? 0;
        var selectedAsioIndex = AsioDriverComboBox?.SelectedIndex ?? 0;
        var selectedDevice = GetSelectedOutputDevice();

        var backends = new List<ZenithRemoteBackendItem>
        {
            new(0, "Zenith WASAPI", "WASAPI", selectedBackendIndex == 0),
            new(1, "ASIO (DSD Nativo)", "ASIO", selectedBackendIndex == 1),
            new(2, "BASS WASAPI", "BASS", selectedBackendIndex == 2),
            new(3, "MPV WASAPI", "MPV", selectedBackendIndex == 3)
        };

        var devices = _outputDevices
            .Select((d, idx) => new ZenithRemoteOutputDevice(
                idx,
                d.Id,
                d.Name,
                d.KindLabel,
                HasDedicatedDacDriver(d),
                idx == selectedOutputIndex))
            .ToList();

        var asioDrivers = _asioDrivers
            .Select((a, idx) => new ZenithRemoteAsioDriver(idx, a.Name, idx == selectedAsioIndex))
            .ToList();

        var dsdOptions = GetCurrentDsdPlaybackOptions();
        var dsdModeIndex = DsdModeComboBox?.SelectedIndex ?? 0;
        var volumeLocked = _asioEngine.IsNativeDsdActive || _wasapiEngine.IsDoPActive;
        var volume = VolumeSlider?.Value ?? 100;
        var volumeLabel = _asioEngine.IsNativeDsdActive
            ? "100% (DSD Nativo)"
            : _wasapiEngine.IsDoPActive
                ? "100% (DoP)"
                : $"{(int)Math.Round(volume)}%";

        var dsdCount = _libraryTracks.Count(t => DsdExtensions.Contains(t.Extension));
        var hiResCount = _libraryTracks.Count(t => HiResPcmExtensions.Contains(t.Extension));

        var activeLyric = string.Empty;
        if (_currentLyricIndex >= 0 && _currentLyricIndex < _syncedLyrics.Count)
        {
            activeLyric = _syncedLyrics[_currentLyricIndex].Text;
        }

        var trackSubtitle = string.IsNullOrWhiteSpace(_currentFilePath)
            ? "Elige una canción desde Biblioteca o en el PC"
            : (Path.GetFileName(Path.GetDirectoryName(_currentFilePath)) ?? Path.GetFileName(_currentFilePath));

        var accentHex = $"#{_dynamicAccentColor.R:X2}{_dynamicAccentColor.G:X2}{_dynamicAccentColor.B:X2}";

        return new ZenithRemoteState(
            TrackTitle: TrackTitleTextBlock?.Text ?? "Sin pista cargada",
            TrackSubtitle: trackSubtitle,
            TrackPath: _currentFilePath ?? string.Empty,
            HasTrack: !string.IsNullOrWhiteSpace(_currentFilePath),
            IsPlaying: isPlaying,
            IsPaused: _isPaused,
            Shuffle: _shuffleEnabled,
            PositionSeconds: Math.Max(0, position.TotalSeconds),
            DurationSeconds: Math.Max(0, duration.TotalSeconds),
            CurrentTimeText: CurrentTimeTextBlock?.Text ?? FormatTime(position),
            DurationText: DurationTextBlock?.Text ?? FormatTime(duration),
            Volume: volume,
            VolumeLocked: volumeLocked,
            VolumeLabel: volumeLabel,
            VuLevel: Math.Clamp(_vuDisplayLevel, 0, 1),
            TransportBadge: TransportBadgeTextBlock?.Text ?? "LISTO",
            Codec: CodecTextBlock?.Text ?? "PCM",
            SampleRate: SampleRateTextBlock?.Text ?? "--",
            BitDepth: BitDepthTextBlock?.Text ?? "--",
            Bitrate: BitrateTextBlock?.Text ?? "--",
            OutputPath: OutputPathTextBlock?.Text ?? "WASAPI",
            StatusMessage: StatusInfoBar?.Message ?? string.Empty,
            BackendIndex: selectedBackendIndex,
            BackendName: GetBackendShortLabel(GetSelectedBackend()),
            Backends: backends,
            OutputDeviceIndex: selectedOutputIndex,
            OutputDeviceName: selectedDevice?.Name ?? "Salida predeterminada",
            OutputDevices: devices,
            AsioDriverIndex: selectedAsioIndex,
            AsioDrivers: asioDrivers,
            ExclusiveMode: ExclusiveModeToggle?.IsOn ?? false,
            HasDedicatedDac: HasDedicatedDacDriver(selectedDevice),
            DsdModeIndex: dsdModeIndex,
            DsdGainDb: dsdOptions.GainDb,
            DsdPcmRate: dsdOptions.TargetPcmSampleRate,
            EqEnabled: EqToggleButton?.IsChecked == true,
            DspBypass: DspBypassToggleButton?.IsChecked == true,
            PreampDb: PreampSlider?.Value ?? 0,
            SubBassDb: SubBassSlider?.Value ?? 0,
            PresenceDb: PresenceSlider?.Value ?? 0,
            AirDb: AirSlider?.Value ?? 0,
            CoverVersion: GetCurrentCoverVersion(),
            HasCover: HasCachedCover(),
            AccentHex: accentHex,
            TotalLibraryTracks: _libraryTracks.Count,
            DsdTracksCount: dsdCount,
            HiResTracksCount: hiResCount,
            ActiveLyricLine: activeLyric,
            RequiresPin: !string.IsNullOrEmpty(_remoteSettings.Pin),
            ServerHostName: Environment.MachineName);
    }

    private ZenithRemoteLibraryResponse BuildRemoteLibraryResponse(string? query, string? filter, int offset, int limit)
    {
        IEnumerable<LibraryTrack> source = _libraryTracks;

        if (string.Equals(filter, "dsd", StringComparison.OrdinalIgnoreCase))
        {
            source = source.Where(t => DsdExtensions.Contains(t.Extension));
        }
        else if (string.Equals(filter, "hires", StringComparison.OrdinalIgnoreCase))
        {
            source = source.Where(t => HiResPcmExtensions.Contains(t.Extension));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var tokens = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            source = source.Where(t => tokens.All(tok =>
                t.Title.Contains(tok, StringComparison.OrdinalIgnoreCase) ||
                t.Folder.Contains(tok, StringComparison.OrdinalIgnoreCase) ||
                t.Format.Contains(tok, StringComparison.OrdinalIgnoreCase) ||
                t.Path.Contains(tok, StringComparison.OrdinalIgnoreCase)));
        }

        var matched = source.ToList();
        var slice = matched
            .Skip(offset)
            .Take(limit)
            .Select(t => new ZenithRemoteTrackItem(
                t.Path,
                t.Title,
                t.Folder,
                t.Format,
                t.Extension,
                !string.IsNullOrWhiteSpace(_currentFilePath) &&
                t.Path.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new ZenithRemoteLibraryResponse(
            TotalLibraryCount: _libraryTracks.Count,
            MatchedCount: matched.Count,
            Offset: offset,
            Limit: limit,
            HasMore: offset + slice.Count < matched.Count,
            Items: slice);
    }

    private async Task<ZenithRemoteActionResult> ExecuteRemoteActionOnUiAsync(string action, JsonElement payload)
    {
        switch (action.Trim().ToLowerInvariant())
        {
            case "toggle_play":
                if (StopButton.IsEnabled)
                {
                    PauseButton_Click(this, new RoutedEventArgs());
                    return new ZenithRemoteActionResult(true, _isPaused ? "Pausado" : "Reproduciendo");
                }

                if (!string.IsNullOrWhiteSpace(_currentFilePath))
                {
                    await StartPlaybackInternalAsync(_currentFilePath, resumePosition: null);
                    return new ZenithRemoteActionResult(true, $"Reproduciendo {TrackTitleTextBlock.Text}");
                }

                if (_libraryTracks.Count > 0)
                {
                    await LoadLibraryTrackAsync(_libraryTracks[0], play: true);
                    return new ZenithRemoteActionResult(true, $"Reproduciendo {_libraryTracks[0].Title}");
                }

                return new ZenithRemoteActionResult(false, "No hay pista cargada en Zenith Audio.");

            case "play":
                if (StopButton.IsEnabled && _isPaused)
                {
                    PauseButton_Click(this, new RoutedEventArgs());
                    return new ZenithRemoteActionResult(true, "Reproducción reanudada");
                }

                if (!string.IsNullOrWhiteSpace(_currentFilePath))
                {
                    await StartPlaybackInternalAsync(_currentFilePath, resumePosition: null);
                    return new ZenithRemoteActionResult(true, "Reproduciendo");
                }

                return new ZenithRemoteActionResult(false, "No hay pista cargada.");

            case "pause":
                if (StopButton.IsEnabled && !_isPaused)
                {
                    PauseButton_Click(this, new RoutedEventArgs());
                }

                return new ZenithRemoteActionResult(true, "Pausado");

            case "stop":
                if (StopButton.IsEnabled)
                {
                    StopButton_Click(this, new RoutedEventArgs());
                }

                return new ZenithRemoteActionResult(true, "Detenido");

            case "next":
                await PlayNextTrackAsync(manual: true);
                return new ZenithRemoteActionResult(true, $"Siguiente: {TrackTitleTextBlock.Text}");

            case "previous":
                await PlayPreviousTrackAsync(manual: true);
                return new ZenithRemoteActionResult(true, $"Anterior: {TrackTitleTextBlock.Text}");

            case "shuffle":
                _shuffleEnabled = !_shuffleEnabled;
                UpdateShuffleButtonVisual();
                return new ZenithRemoteActionResult(true, _shuffleEnabled ? "Aleatorio activado" : "Orden secuencial");

            case "seek":
                if (payload.TryGetProperty("seconds", out var secProp) && secProp.TryGetDouble(out var sec))
                {
                    var target = TimeSpan.FromSeconds(Math.Max(0, sec));
                    SeekFallbackPlayer(target);
                    return new ZenithRemoteActionResult(true, $"Posición: {FormatTime(target)}");
                }

                return new ZenithRemoteActionResult(false, "Tiempo inválido.");

            case "volume":
                if (_asioEngine.IsNativeDsdActive || _wasapiEngine.IsDoPActive)
                {
                    return new ZenithRemoteActionResult(false, "Volumen bloqueado en 100% durante DSD nativo / DoP.");
                }

                if (payload.TryGetProperty("value", out var volProp) && volProp.TryGetDouble(out var vol))
                {
                    var clamped = Math.Clamp(vol, 0, 100);
                    VolumeSlider.Value = clamped;
                    return new ZenithRemoteActionResult(true, $"Volumen {(int)Math.Round(clamped)}%");
                }

                return new ZenithRemoteActionResult(false, "Valor de volumen inválido.");

            case "play_track":
                LibraryTrack? targetTrack = null;
                if (payload.TryGetProperty("path", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
                {
                    var requestedPath = pathProp.GetString() ?? string.Empty;
                    targetTrack = _libraryTracks.FirstOrDefault(t => t.Path.Equals(requestedPath, StringComparison.OrdinalIgnoreCase));
                }
                else if (payload.TryGetProperty("query", out var queryProp) && queryProp.ValueKind == JsonValueKind.String)
                {
                    targetTrack = FindBestLibraryTrackMatch(queryProp.GetString() ?? string.Empty);
                }

                if (targetTrack is null)
                {
                    return new ZenithRemoteActionResult(false, "Pista no encontrada en la biblioteca.");
                }

                StopCurrentPlaybackForTrackChange();
                await LoadLibraryTrackAsync(targetTrack, play: true);
                return new ZenithRemoteActionResult(true, $"Reproduciendo {targetTrack.Title}");

            case "set_output":
                if (payload.TryGetProperty("index", out var outIdxProp) && outIdxProp.TryGetInt32(out var outIdx) &&
                    outIdx >= 0 && outIdx < _outputDevices.Count)
                {
                    OutputDeviceComboBox.SelectedIndex = outIdx;
                    return new ZenithRemoteActionResult(true, $"Salida: {_outputDevices[outIdx].Name}");
                }

                return new ZenithRemoteActionResult(false, "Dispositivo de salida no válido.");

            case "set_backend":
                if (payload.TryGetProperty("index", out var beIdxProp) && beIdxProp.TryGetInt32(out var beIdx) &&
                    beIdx >= 0 && beIdx < BackendComboBox.Items.Count)
                {
                    if (beIdx == 1 && _asioDrivers.Count == 0)
                    {
                        return new ZenithRemoteActionResult(false, "No hay drivers ASIO registrados en Windows.");
                    }

                    BackendComboBox.SelectedIndex = beIdx;
                    return new ZenithRemoteActionResult(true, $"Motor: {GetBackendShortLabel(GetSelectedBackend())}");
                }

                return new ZenithRemoteActionResult(false, "Motor no válido.");

            case "set_asio_driver":
                if (payload.TryGetProperty("index", out var asioIdxProp) && asioIdxProp.TryGetInt32(out var asioIdx) &&
                    AsioDriverComboBox is not null && asioIdx >= 0 && asioIdx < _asioDrivers.Count)
                {
                    AsioDriverComboBox.SelectedIndex = asioIdx;
                    return new ZenithRemoteActionResult(true, $"ASIO: {_asioDrivers[asioIdx].Name}");
                }

                return new ZenithRemoteActionResult(false, "Driver ASIO no válido.");

            case "set_exclusive":
                var wantExclusive = payload.TryGetProperty("enabled", out var exProp) && exProp.ValueKind == JsonValueKind.True;
                if (wantExclusive && !HasDedicatedDacDriver(GetSelectedOutputDevice()))
                {
                    return new ZenithRemoteActionResult(false, "Modo Exclusivo bloqueado: selecciona un DAC USB con controlador dedicado.");
                }

                ExclusiveModeToggle.IsOn = wantExclusive;
                return new ZenithRemoteActionResult(true, wantExclusive ? "Modo Exclusivo activado" : "Modo Exclusivo desactivado");

            case "set_dsd":
                if (payload.TryGetProperty("mode", out var dsdModeProp) && dsdModeProp.TryGetInt32(out var dsdModeIdx) &&
                    DsdModeComboBox is not null && dsdModeIdx >= 0 && dsdModeIdx < DsdModeComboBox.Items.Count)
                {
                    DsdModeComboBox.SelectedIndex = dsdModeIdx;
                }

                if (payload.TryGetProperty("gain", out var dsdGainProp) && dsdGainProp.TryGetDouble(out var dsdGainVal) && DsdGainComboBox is not null)
                {
                    var gainTag = ((int)Math.Round(dsdGainVal)).ToString(CultureInfo.InvariantCulture);
                    for (var i = 0; i < DsdGainComboBox.Items.Count; i++)
                    {
                        if (DsdGainComboBox.Items[i] is ComboBoxItem { Tag: string tag } && tag == gainTag)
                        {
                            DsdGainComboBox.SelectedIndex = i;
                            break;
                        }
                    }
                }

                if (payload.TryGetProperty("rate", out var dsdRateProp) && dsdRateProp.TryGetInt32(out var dsdRateVal) && DsdPcmRateComboBox is not null)
                {
                    var rateTag = dsdRateVal.ToString(CultureInfo.InvariantCulture);
                    for (var i = 0; i < DsdPcmRateComboBox.Items.Count; i++)
                    {
                        if (DsdPcmRateComboBox.Items[i] is ComboBoxItem { Tag: string tag } && tag == rateTag)
                        {
                            DsdPcmRateComboBox.SelectedIndex = i;
                            break;
                        }
                    }
                }

                return new ZenithRemoteActionResult(true, "Configuración DSD actualizada");

            case "set_tone":
                if (payload.TryGetProperty("preset", out var presetProp) && presetProp.ValueKind == JsonValueKind.String)
                {
                    var preset = (presetProp.GetString() ?? string.Empty).Trim().ToLowerInvariant();
                    switch (preset)
                    {
                        case "puro":
                            ApplyToneProfile("Audio puro (Remoto)", eqEnabled: false, dspBypassed: true, preampDb: 0, subBassDb: 0, presenceDb: 0, airDb: 0);
                            return new ZenithRemoteActionResult(true, "DSP en Bypass (Audio puro)");
                        case "calido":
                            ApplyToneProfile("Cálido musical (Remoto)", eqEnabled: true, dspBypassed: false, preampDb: -2.0, subBassDb: 2.5, presenceDb: 0.5, airDb: -0.5);
                            return new ZenithRemoteActionResult(true, "Perfil Cálido aplicado");
                        case "voces":
                            ApplyToneProfile("Voces claras (Remoto)", eqEnabled: true, dspBypassed: false, preampDb: -2.0, subBassDb: 0.0, presenceDb: 2.5, airDb: 1.0);
                            return new ZenithRemoteActionResult(true, "Perfil Voces aplicadas");
                        case "aireado":
                            ApplyToneProfile("Aireado Hi-Fi (Remoto)", eqEnabled: true, dspBypassed: false, preampDb: -2.0, subBassDb: 0.5, presenceDb: 1.0, airDb: 2.5);
                            return new ZenithRemoteActionResult(true, "Perfil Aireado aplicado");
                        case "estudio":
                            ApplyToneProfile("Estudio neutral (Remoto)", eqEnabled: true, dspBypassed: false, preampDb: -1.0, subBassDb: 0.5, presenceDb: 0.5, airDb: 0.5);
                            return new ZenithRemoteActionResult(true, "Perfil Estudio aplicado");
                    }
                }

                var bypass = payload.TryGetProperty("dspBypass", out var bpProp) ? bpProp.ValueKind == JsonValueKind.True : (DspBypassToggleButton?.IsChecked == true);
                var eqOn = payload.TryGetProperty("eqEnabled", out var eqProp) ? eqProp.ValueKind == JsonValueKind.True : (EqToggleButton?.IsChecked == true);
                var preamp = payload.TryGetProperty("preamp", out var prProp) && prProp.TryGetDouble(out var prV) ? Math.Clamp(prV, -12, 12) : (PreampSlider?.Value ?? 0);
                var sub = payload.TryGetProperty("subBass", out var subProp) && subProp.TryGetDouble(out var subV) ? Math.Clamp(subV, -6, 6) : (SubBassSlider?.Value ?? 0);
                var pres = payload.TryGetProperty("presence", out var psProp) && psProp.TryGetDouble(out var psV) ? Math.Clamp(psV, -6, 6) : (PresenceSlider?.Value ?? 0);
                var air = payload.TryGetProperty("air", out var airProp) && airProp.TryGetDouble(out var airV) ? Math.Clamp(airV, -6, 6) : (AirSlider?.Value ?? 0);

                ApplyToneProfile("Control Remoto", eqEnabled: bypass ? false : eqOn, dspBypassed: bypass, preampDb: preamp, subBassDb: sub, presenceDb: pres, airDb: air);
                return new ZenithRemoteActionResult(true, bypass ? "DSP en Bypass puro" : "Control de tono actualizado");

            case "refresh_devices":
                RefreshAsioDrivers();
                await RefreshOutputDevicesAsync();
                return new ZenithRemoteActionResult(true, "Dispositivos de audio actualizados");

            default:
                return new ZenithRemoteActionResult(false, $"Acción '{action}' no reconocida.");
        }
    }

    private sealed class RemoteBridgeAdapter(MainWindow owner) : IZenithRemoteBridge
    {
        public Task<ZenithRemoteState> GetStateAsync()
        {
            return owner.RunOnUiThreadAsync(() => Task.FromResult(owner.BuildRemoteStateSnapshot()));
        }

        public Task<ZenithRemoteLibraryResponse> QueryLibraryAsync(string? query, string? filter, int offset, int limit)
        {
            return owner.RunOnUiThreadAsync(() => Task.FromResult(owner.BuildRemoteLibraryResponse(query, filter, offset, limit)));
        }

        public Task<ZenithRemoteActionResult> ExecuteActionAsync(string action, JsonElement payload)
        {
            return owner.RunOnUiThreadAsync(() => owner.ExecuteRemoteActionOnUiAsync(action, payload));
        }

        public ZenithRemoteCoverData? GetCurrentCover()
        {
            return owner.GetCachedCoverData();
        }
    }
}
