using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace ZenithAudio.Core.Updates;

public sealed class ZenithUpdateSettings
{
    public bool CheckOnStartup { get; set; } = true;
    public bool AutoDownloadAndInstall { get; set; }

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZenithAudio",
            "update.settings.json");

    public static ZenithUpdateSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<ZenithUpdateSettings>(json);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back to defaults.
        }

        return new ZenithUpdateSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignore non-critical settings write errors.
        }
    }
}

public sealed record GitHubReleaseUpdate(
    Version CurrentVersion,
    Version LatestVersion,
    string TagName,
    string ReleaseTitle,
    string ReleaseNotes,
    string ReleasePageUrl,
    string? InstallerFileName,
    string? InstallerDownloadUrl,
    long InstallerSizeBytes,
    bool IsUpdateAvailable);

public sealed record UpdateDownloadProgress(
    long BytesDownloaded,
    long TotalBytes)
{
    public double Percent => TotalBytes > 0
        ? Math.Clamp((BytesDownloaded * 100.0) / TotalBytes, 0.0, 100.0)
        : 0.0;
}

public sealed class GitHubUpdateService
{
    public const string RepoOwner = "xn1llox";
    public const string RepoName = "Zenith-Audio-Player";
    private const string LatestReleaseApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZenithAudio", GetCurrentVersionString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static Version GetCurrentVersion()
    {
        var raw = Assembly.GetExecutingAssembly().GetName().Version;
        if (raw is null)
        {
            return new Version(1, 0, 8);
        }

        return new Version(
            Math.Max(0, raw.Major),
            Math.Max(0, raw.Minor),
            Math.Max(0, raw.Build));
    }

    public static string GetCurrentVersionString()
    {
        var v = GetCurrentVersion();
        return $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public async Task<GitHubReleaseUpdate> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GitHub respondió HTTP {(int)response.StatusCode} al comprobar actualizaciones.");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = TryGetString(root, "tag_name") ?? string.Empty;
        var releaseTitle = TryGetString(root, "name");
        if (string.IsNullOrWhiteSpace(releaseTitle))
        {
            releaseTitle = $"Zenith Audio {tagName}";
        }

        var releaseNotes = TryGetString(root, "body") ?? string.Empty;
        var htmlUrl = TryGetString(root, "html_url") ?? $"https://github.com/{RepoOwner}/{RepoName}/releases";
        var latestVersion = ParseReleaseVersion(tagName) ?? currentVersion;

        string? bestAssetName = null;
        string? bestAssetUrl = null;
        long bestAssetSize = 0;
        var bestScore = -1;

        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = TryGetString(asset, "name");
                var downloadUrl = TryGetString(asset, "browser_download_url");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(downloadUrl))
                {
                    continue;
                }

                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var score = 10;
                if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                {
                    score += 20;
                }

                if (name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("x64", StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                }

                if (name.StartsWith("ZenithAudio", StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestAssetName = name;
                    bestAssetUrl = downloadUrl;
                    bestAssetSize = asset.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var parsedSize)
                        ? parsedSize
                        : 0;
                }
            }
        }

        var isNewer = latestVersion > currentVersion;

        return new GitHubReleaseUpdate(
            CurrentVersion: currentVersion,
            LatestVersion: latestVersion,
            TagName: tagName,
            ReleaseTitle: releaseTitle,
            ReleaseNotes: releaseNotes,
            ReleasePageUrl: htmlUrl,
            InstallerFileName: bestAssetName,
            InstallerDownloadUrl: bestAssetUrl,
            InstallerSizeBytes: bestAssetSize,
            IsUpdateAvailable: isNewer);
    }

    public async Task<string> DownloadInstallerAsync(
        GitHubReleaseUpdate update,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(update.InstallerDownloadUrl))
        {
            throw new InvalidOperationException("Este release en GitHub no contiene un instalador .exe compatible.");
        }

        var safeFileName = string.IsNullOrWhiteSpace(update.InstallerFileName)
            ? $"ZenithAudio_v{update.LatestVersion}_Setup_win-x64.exe"
            : Path.GetFileName(update.InstallerFileName);

        var updatesDir = Path.Combine(Path.GetTempPath(), "ZenithAudioUpdates");
        Directory.CreateDirectory(updatesDir);

        var finalPath = Path.Combine(updatesDir, safeFileName);
        var partPath = finalPath + ".part";

        if (File.Exists(partPath))
        {
            File.Delete(partPath);
        }

        try
        {
            using var response = await HttpClient.GetAsync(update.InstallerDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? update.InstallerSizeBytes;

            await using (var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                var lastReportTick = Environment.TickCount64;

                while (true)
                {
                    var read = await remoteStream.ReadAsync(buffer, cancellationToken);
                    if (read <= 0)
                    {
                        break;
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;

                    var now = Environment.TickCount64;
                    if (now - lastReportTick >= 120)
                    {
                        lastReportTick = now;
                        progress?.Report(new UpdateDownloadProgress(downloaded, totalBytes));
                    }
                }

                await fileStream.FlushAsync(cancellationToken);
                progress?.Report(new UpdateDownloadProgress(downloaded, totalBytes > 0 ? totalBytes : downloaded));
            }

            if (update.InstallerSizeBytes > 0)
            {
                var actualSize = new FileInfo(partPath).Length;
                if (actualSize != update.InstallerSizeBytes)
                {
                    throw new InvalidOperationException($"Descarga incompleta ({actualSize} / {update.InstallerSizeBytes} bytes). Intenta nuevamente.");
                }
            }

            File.Move(partPath, finalPath, overwrite: true);
            return finalPath;
        }
        catch
        {
            try
            {
                if (File.Exists(partPath))
                {
                    File.Delete(partPath);
                }
            }
            catch
            {
            }

            throw;
        }
    }

    public static void LaunchInstaller(string installerPath, bool silent)
    {
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("No se encontró el instalador descargado.", installerPath);
        }

        var args = silent
            ? "/SP- /SILENT /CLOSEAPPLICATIONS /NORESTART"
            : "/SP- /CLOSEAPPLICATIONS /NORESTART";

        var psi = new ProcessStartInfo(installerPath)
        {
            Arguments = args,
            UseShellExecute = true
        };

        Process.Start(psi);
    }

    public static Version? ParseReleaseVersion(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var cleaned = tagName.Trim().TrimStart('v', 'V');
        var dashIdx = cleaned.IndexOf('-');
        if (dashIdx > 0)
        {
            cleaned = cleaned[..dashIdx];
        }

        if (!Version.TryParse(cleaned, out var parsed))
        {
            return null;
        }

        return new Version(
            Math.Max(0, parsed.Major),
            Math.Max(0, parsed.Minor),
            Math.Max(0, parsed.Build));
    }

    private static string? TryGetString(JsonElement obj, string propertyName)
    {
        return obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()?.Trim()
            : null;
    }
}
