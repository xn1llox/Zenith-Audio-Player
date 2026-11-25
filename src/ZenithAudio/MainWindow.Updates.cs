using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using ZenithAudio.Core.Updates;

namespace ZenithAudio;

public sealed partial class MainWindow
{
    private readonly GitHubUpdateService _updateService = new();
    private ZenithUpdateSettings _updateSettings = new();
    private GitHubReleaseUpdate? _availableUpdate;
    private CancellationTokenSource? _updateDownloadCts;
    private bool _isCheckingUpdates;
    private bool _isDownloadingUpdate;
    private bool _isLoadingUpdateSettings;

    private void InitializeAutoUpdater()
    {
        _updateSettings = ZenithUpdateSettings.Load();
        _isLoadingUpdateSettings = true;
        try
        {
            if (AppVersionBadgeTextBlock is not null)
            {
                AppVersionBadgeTextBlock.Text = $"v{GitHubUpdateService.GetCurrentVersionString()}";
            }

            if (AutoCheckUpdatesToggle is not null)
            {
                AutoCheckUpdatesToggle.IsOn = _updateSettings.CheckOnStartup;
            }

            if (AutoInstallUpdatesToggle is not null)
            {
                AutoInstallUpdatesToggle.IsOn = _updateSettings.AutoDownloadAndInstall;
            }

            if (UpdateStatusTextBlock is not null)
            {
                UpdateStatusTextBlock.Text = _updateSettings.CheckOnStartup
                    ? "Comprobación automática al iniciar activada."
                    : "Pulsa Buscar ahora para comprobar nuevas versiones en GitHub.";
            }
        }
        finally
        {
            _isLoadingUpdateSettings = false;
        }

        if (_updateSettings.CheckOnStartup)
        {
            _ = CheckForUpdatesOnStartupAsync();
        }
    }

    private void DisposeAutoUpdater()
    {
        try
        {
            _updateDownloadCts?.Cancel();
            _updateDownloadCts?.Dispose();
            _updateDownloadCts = null;
        }
        catch
        {
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            // Give the main UI and audio engine a couple seconds to settle first.
            await Task.Delay(2500);
            await CheckForUpdatesInternalAsync(isAutomaticStartup: true);
        }
        catch
        {
            // Startup check must never disrupt playback if offline.
        }
    }

    private void AutoCheckUpdatesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingUpdateSettings || AutoCheckUpdatesToggle is null)
        {
            return;
        }

        _updateSettings.CheckOnStartup = AutoCheckUpdatesToggle.IsOn;
        _updateSettings.Save();
    }

    private void AutoInstallUpdatesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoadingUpdateSettings || AutoInstallUpdatesToggle is null)
        {
            return;
        }

        _updateSettings.AutoDownloadAndInstall = AutoInstallUpdatesToggle.IsOn;
        _updateSettings.Save();
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesInternalAsync(isAutomaticStartup: false);
    }

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloadingUpdate)
        {
            _updateDownloadCts?.Cancel();
            return;
        }

        if (_availableUpdate is null || !_availableUpdate.IsUpdateAvailable)
        {
            await CheckForUpdatesInternalAsync(isAutomaticStartup: false);
            return;
        }

        await DownloadAndInstallUpdateAsync(_availableUpdate, silentInstall: true);
    }

    private void OpenDeveloperWebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.espinozainformatica.cl") { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private async Task CheckForUpdatesInternalAsync(bool isAutomaticStartup)
    {
        if (_isCheckingUpdates || _isDownloadingUpdate)
        {
            return;
        }

        _isCheckingUpdates = true;
        if (CheckUpdatesButton is not null)
        {
            CheckUpdatesButton.IsEnabled = false;
        }

        if (UpdateStatusTextBlock is not null)
        {
            UpdateStatusTextBlock.Text = "Buscando actualizaciones en GitHub...";
        }

        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            _availableUpdate = result;

            if (!result.IsUpdateAvailable)
            {
                if (UpdateStatusTextBlock is not null)
                {
                    UpdateStatusTextBlock.Text = $"Zenith Audio está actualizado (v{result.CurrentVersion}).";
                }

                if (InstallUpdateButton is not null)
                {
                    InstallUpdateButton.Visibility = Visibility.Collapsed;
                }

                if (!isAutomaticStartup)
                {
                    StatusInfoBar.Severity = InfoBarSeverity.Success;
                    StatusInfoBar.Message = $"Ya tienes la última versión de Zenith Audio (v{result.CurrentVersion}).";
                }

                return;
            }

            if (UpdateStatusTextBlock is not null)
            {
                var sizeMb = result.InstallerSizeBytes > 0
                    ? $" ({result.InstallerSizeBytes / (1024.0 * 1024.0):0.0} MB)"
                    : string.Empty;
                UpdateStatusTextBlock.Text = $"Nueva versión disponible: v{result.LatestVersion}{sizeMb}";
            }

            if (InstallUpdateButton is not null)
            {
                InstallUpdateButton.Visibility = Visibility.Visible;
                InstallUpdateButton.IsEnabled = true;
            }

            if (InstallUpdateButtonTextBlock is not null)
            {
                InstallUpdateButtonTextBlock.Text = $"Descargar e instalar v{result.LatestVersion}";
            }

            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = $"Actualización disponible en GitHub: Zenith Audio v{result.LatestVersion}";

            if (isAutomaticStartup && _updateSettings.AutoDownloadAndInstall && !string.IsNullOrWhiteSpace(result.InstallerDownloadUrl))
            {
                await DownloadAndInstallUpdateAsync(result, silentInstall: true);
                return;
            }

            await ShowUpdateAvailableDialogAsync(result);
        }
        catch (Exception ex)
        {
            if (UpdateStatusTextBlock is not null)
            {
                UpdateStatusTextBlock.Text = isAutomaticStartup
                    ? "No se pudo comprobar actualizaciones al iniciar (sin conexión)."
                    : $"Error al buscar actualizaciones: {ex.Message}";
            }

            if (!isAutomaticStartup)
            {
                StatusInfoBar.Severity = InfoBarSeverity.Warning;
                StatusInfoBar.Message = $"No se pudieron comprobar actualizaciones: {ex.Message}";
            }
        }
        finally
        {
            _isCheckingUpdates = false;
            if (CheckUpdatesButton is not null)
            {
                CheckUpdatesButton.IsEnabled = !_isDownloadingUpdate;
            }
        }
    }

    private async Task ShowUpdateAvailableDialogAsync(GitHubReleaseUpdate update)
    {
        if (Content?.XamlRoot is null)
        {
            return;
        }

        var notesPreview = string.IsNullOrWhiteSpace(update.ReleaseNotes)
            ? "Mejoras y correcciones disponibles en GitHub Releases."
            : update.ReleaseNotes.Trim();

        if (notesPreview.Length > 750)
        {
            notesPreview = notesPreview[..750] + "\n...";
        }

        var sizeText = update.InstallerSizeBytes > 0
            ? $" ({update.InstallerSizeBytes / (1024.0 * 1024.0):0.0} MB)"
            : string.Empty;

        var bodyStack = new StackPanel { Spacing = 10 };
        bodyStack.Children.Add(new TextBlock
        {
            Text = $"Versión instalada: v{update.CurrentVersion}  →  Nueva versión: v{update.LatestVersion}{sizeText}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        bodyStack.Children.Add(new ScrollViewer
        {
            MaxHeight = 220,
            Content = new TextBlock
            {
                Text = notesPreview,
                FontSize = 12,
                TextWrapping = TextWrapping.WrapWholeWords
            }
        });

        var dialog = new ContentDialog
        {
            Title = $"Actualización disponible: {update.ReleaseTitle}",
            Content = bodyStack,
            PrimaryButtonText = !string.IsNullOrWhiteSpace(update.InstallerDownloadUrl)
                ? "Descargar e instalar"
                : "Abrir GitHub",
            SecondaryButtonText = "Ver en GitHub",
            CloseButtonText = "Más tarde",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.Primary)
        {
            if (!string.IsNullOrWhiteSpace(update.InstallerDownloadUrl))
            {
                await DownloadAndInstallUpdateAsync(update, silentInstall: true);
            }
            else
            {
                Process.Start(new ProcessStartInfo(update.ReleasePageUrl) { UseShellExecute = true });
            }
        }
        else if (choice == ContentDialogResult.Secondary)
        {
            Process.Start(new ProcessStartInfo(update.ReleasePageUrl) { UseShellExecute = true });
        }
    }

    private async Task DownloadAndInstallUpdateAsync(GitHubReleaseUpdate update, bool silentInstall)
    {
        if (_isDownloadingUpdate)
        {
            return;
        }

        _isDownloadingUpdate = true;
        _updateDownloadCts?.Dispose();
        _updateDownloadCts = new CancellationTokenSource();

        if (CheckUpdatesButton is not null)
        {
            CheckUpdatesButton.IsEnabled = false;
        }

        if (InstallUpdateButton is not null)
        {
            InstallUpdateButton.Visibility = Visibility.Visible;
            InstallUpdateButton.IsEnabled = true;
        }

        if (InstallUpdateButtonTextBlock is not null)
        {
            InstallUpdateButtonTextBlock.Text = "Cancelar descarga";
        }

        if (UpdateDownloadProgressPanel is not null)
        {
            UpdateDownloadProgressPanel.Visibility = Visibility.Visible;
        }

        if (UpdateDownloadProgressBar is not null)
        {
            UpdateDownloadProgressBar.Value = 0;
        }

        if (UpdateStatusTextBlock is not null)
        {
            UpdateStatusTextBlock.Text = $"Descargando Zenith Audio v{update.LatestVersion}...";
        }

        StatusInfoBar.Severity = InfoBarSeverity.Informational;
        StatusInfoBar.Message = $"Descargando actualización v{update.LatestVersion} desde GitHub...";

        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (UpdateDownloadProgressBar is not null)
                {
                    UpdateDownloadProgressBar.Value = p.Percent;
                }

                var downMb = p.BytesDownloaded / (1024.0 * 1024.0);
                var totalMb = p.TotalBytes / (1024.0 * 1024.0);
                if (UpdateDownloadProgressTextBlock is not null)
                {
                    UpdateDownloadProgressTextBlock.Text = p.TotalBytes > 0
                        ? $"{downMb:0.1} MB / {totalMb:0.1} MB ({p.Percent:0}%)"
                        : $"{downMb:0.1} MB descargados";
                }
            });
        });

        try
        {
            var installerPath = await _updateService.DownloadInstallerAsync(update, progress, _updateDownloadCts.Token);

            if (UpdateStatusTextBlock is not null)
            {
                UpdateStatusTextBlock.Text = "Descarga completa. Iniciando instalador...";
            }

            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.Message = $"Instalador v{update.LatestVersion} listo. Cerrando Zenith Audio para actualizar...";

            // Stop playback cleanly so DAC/ASIO/WASAPI handles are released before installer runs.
            StopCurrentPlaybackForTrackChange();
            _remoteServer?.Dispose();

            GitHubUpdateService.LaunchInstaller(installerPath, silent: silentInstall);
            await Task.Delay(350);
            Close();
        }
        catch (OperationCanceledException)
        {
            if (UpdateStatusTextBlock is not null)
            {
                UpdateStatusTextBlock.Text = $"Descarga cancelada. v{update.LatestVersion} sigue disponible.";
            }

            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Message = "Descarga de actualización cancelada.";
        }
        catch (Exception ex)
        {
            if (UpdateStatusTextBlock is not null)
            {
                UpdateStatusTextBlock.Text = $"Error al descargar/instalar: {ex.Message}";
            }

            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Message = $"Error en actualización automática: {ex.Message}";
        }
        finally
        {
            _isDownloadingUpdate = false;
            if (CheckUpdatesButton is not null)
            {
                CheckUpdatesButton.IsEnabled = true;
            }

            if (UpdateDownloadProgressPanel is not null)
            {
                UpdateDownloadProgressPanel.Visibility = Visibility.Collapsed;
            }

            if (InstallUpdateButtonTextBlock is not null && _availableUpdate?.IsUpdateAvailable == true)
            {
                InstallUpdateButtonTextBlock.Text = $"Descargar e instalar v{_availableUpdate.LatestVersion}";
            }
        }
    }
}
