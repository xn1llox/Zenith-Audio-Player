using System.Text.Json;

namespace ZenithAudio.Core.Remote;

public sealed class ZenithRemoteSettings
{
    public const int DefaultPort = 8787;

    public int Port { get; set; } = DefaultPort;
    public bool AllowLan { get; set; } = true;
    public bool AutoStart { get; set; }
    public string Pin { get; set; } = string.Empty;

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZenithAudio",
            "remote.settings.json");

    public static ZenithRemoteSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<ZenithRemoteSettings>(json);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back to defaults if file is unreadable.
        }

        return new ZenithRemoteSettings();
    }

    public void Save()
    {
        Normalize();
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public void Normalize()
    {
        Port = Math.Clamp(Port, 1024, 65535);
        Pin = (Pin ?? string.Empty).Trim();
        if (Pin.Length > 16)
        {
            Pin = Pin[..16];
        }
    }
}
