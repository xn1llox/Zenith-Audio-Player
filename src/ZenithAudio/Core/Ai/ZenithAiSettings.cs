using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZenithAudio.Core.Ai;

public sealed record ZenithAiSettings(
    string Provider,
    string Endpoint,
    string Model,
    string ApiKey)
{
    public const string DefaultProvider = "NVIDIA NIM";
    public const string DefaultEndpoint = "https://integrate.api.nvidia.com/v1/chat/completions";
    public const string DefaultModel = "nvidia/nemotron-3-super-120b-a12b";
    private const string LegacyDefaultModel = "google/gemma-4-31b-it";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string UserSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ZenithAudio",
        "zenithai.settings.json");

    public static ZenithAiSettings Default => new(DefaultProvider, DefaultEndpoint, DefaultModel, string.Empty);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(Model) &&
        !string.IsNullOrWhiteSpace(ApiKey);

    public static ZenithAiSettings Load()
    {
        var settings = LoadBundledDefaults();

        if (File.Exists(UserSettingsPath))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<ZenithAiSettings>(File.ReadAllText(UserSettingsPath), JsonOptions);
                if (saved is not null)
                {
                    var savedSettings = MigrateLegacyDefaultModel(saved with { ApiKey = UnprotectApiKey(saved.ApiKey) });
                    settings = Merge(settings, savedSettings);
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        var envKey = Environment.GetEnvironmentVariable("NVIDIA_API_KEY")
            ?? Environment.GetEnvironmentVariable("NVIDIA_NIM_API_KEY")
            ?? Environment.GetEnvironmentVariable("ZENITHAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            settings = settings with { ApiKey = envKey.Trim() };
        }

        var markdownSettings = LoadFromNvidiaMarkdown();
        if (markdownSettings is not null && string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            settings = Merge(settings, markdownSettings);
        }

        return settings;
    }

    public static void Save(ZenithAiSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(UserSettingsPath)!);
        var persisted = settings with { ApiKey = ProtectApiKey(settings.ApiKey) };
        File.WriteAllText(UserSettingsPath, JsonSerializer.Serialize(persisted, JsonOptions));
    }

    private static ZenithAiSettings Merge(ZenithAiSettings baseSettings, ZenithAiSettings overrideSettings)
    {
        return new ZenithAiSettings(
            string.IsNullOrWhiteSpace(overrideSettings.Provider) ? baseSettings.Provider : overrideSettings.Provider,
            string.IsNullOrWhiteSpace(overrideSettings.Endpoint) ? baseSettings.Endpoint : overrideSettings.Endpoint,
            string.IsNullOrWhiteSpace(overrideSettings.Model) ? baseSettings.Model : overrideSettings.Model,
            string.IsNullOrWhiteSpace(overrideSettings.ApiKey) ? baseSettings.ApiKey : overrideSettings.ApiKey);
    }

    private static ZenithAiSettings MigrateLegacyDefaultModel(ZenithAiSettings settings)
    {
        var isDefaultNvidiaProvider = settings.Provider.Equals(DefaultProvider, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(settings.Provider);
        var isDefaultEndpoint = settings.Endpoint.Equals(DefaultEndpoint, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(settings.Endpoint);
        var isLegacyDefaultModel = settings.Model.Equals(LegacyDefaultModel, StringComparison.OrdinalIgnoreCase);

        return isDefaultNvidiaProvider && isDefaultEndpoint && isLegacyDefaultModel
            ? settings with { Model = DefaultModel }
            : settings;
    }

    private static ZenithAiSettings LoadBundledDefaults()
    {
        var path = FindProjectFile("zenithai.defaults.json");
        if (path is null)
        {
            return Default;
        }

        try
        {
            return JsonSerializer.Deserialize<ZenithAiSettings>(File.ReadAllText(path), JsonOptions) ?? Default;
        }
        catch (JsonException)
        {
            return Default;
        }
        catch (IOException)
        {
            return Default;
        }
    }

    private static ZenithAiSettings? LoadFromNvidiaMarkdown()
    {
        var configPath = FindProjectFile("API Code Nvidia NIM.md");
        if (configPath is null)
        {
            return null;
        }

        var text = File.ReadAllText(configPath);
        var keyMatch = Regex.Match(text, "Authorization:\\s*Bearer\\s+([^\"'\\s]+)", RegexOptions.IgnoreCase);
        var modelMatch = Regex.Match(text, "\"model\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
        var urlMatch = Regex.Match(text, "invoke_url=['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase);

        var apiKey = keyMatch.Success ? keyMatch.Groups[1].Value.Trim() : string.Empty;
        if (apiKey.Contains('$') || apiKey.Contains('<') || apiKey.Contains('{'))
        {
            apiKey = string.Empty;
        }

        return new ZenithAiSettings(
            DefaultProvider,
            urlMatch.Success ? urlMatch.Groups[1].Value.Trim() : DefaultEndpoint,
            modelMatch.Success ? modelMatch.Groups[1].Value.Trim() : DefaultModel,
            apiKey);
    }

    private static string ProtectApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("dpapi:", StringComparison.Ordinal))
        {
            return apiKey;
        }

        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return "dpapi:" + Convert.ToBase64String(protectedBytes);
    }

    private static string UnprotectApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || !apiKey.StartsWith("dpapi:", StringComparison.Ordinal))
        {
            return apiKey;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(apiKey["dpapi:".Length..]);
            var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static string? FindProjectFile(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && current is not null; i++)
        {
            var candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        var workingDirectoryCandidate = Path.Combine(Directory.GetCurrentDirectory(), fileName);
        return File.Exists(workingDirectoryCandidate) ? workingDirectoryCandidate : null;
    }
}
