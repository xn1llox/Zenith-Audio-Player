using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ZenithAudio.Core.Ai;

public sealed record ZenithAiProviderPreset(
    string Name,
    string Endpoint,
    string SuggestedModel,
    bool RequiresApiKey,
    string Hint)
{
    public override string ToString() => Name;
}

public sealed record ZenithAiModelInfo(
    string Id,
    string Owner,
    string ContextBadge)
{
    public string Subtitle => string.IsNullOrWhiteSpace(Owner) ? "Modelo disponible" : Owner;
}

public sealed class ZenithAiClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public static IReadOnlyList<ZenithAiProviderPreset> ProviderPresets { get; } =
    [
        new("NVIDIA NIM", "https://integrate.api.nvidia.com/v1/chat/completions", "nvidia/nemotron-3-super-120b-a12b", true, "API gratuita/empresarial de NVIDIA NIM"),
        new("OpenAI", "https://api.openai.com/v1/chat/completions", "gpt-4o-mini", true, "API oficial de OpenAI"),
        new("OpenRouter", "https://openrouter.ai/api/v1/chat/completions", "google/gemini-2.5-flash", true, "Acceso unificado a cientos de modelos"),
        new("Groq", "https://api.groq.com/openai/v1/chat/completions", "llama-3.3-70b-versatile", true, "Inferencia ultrarrápida en Groq"),
        new("Google Gemini (OpenAI compat)", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", "gemini-2.5-flash", true, "Endpoint OpenAI-compatible de Google AI Studio"),
        new("DeepSeek", "https://api.deepseek.com/v1/chat/completions", "deepseek-chat", true, "Modelos DeepSeek Chat y Reasoner"),
        new("Mistral AI", "https://api.mistral.ai/v1/chat/completions", "mistral-small-latest", true, "API oficial de Mistral"),
        new("Together AI", "https://api.together.xyz/v1/chat/completions", "meta-llama/Llama-3.3-70B-Instruct-Turbo", true, "Modelos open-weights en Together"),
        new("Ollama (Local)", "http://localhost:11434/v1/chat/completions", "llama3.2", false, "Modelos locales en tu PC (sin API key)"),
        new("LM Studio (Local)", "http://localhost:1234/v1/chat/completions", "local-model", false, "Servidor local de LM Studio (sin API key)"),
        new("Personalizado", "", "", true, "Cualquier servidor compatible con OpenAI Chat Completions")
    ];

    public ZenithAiSettings Settings { get; private set; } = ZenithAiSettings.Load();

    public bool IsConfigured => Settings.IsConfigured;

    public void ReloadSettings()
    {
        Settings = ZenithAiSettings.Load();
    }

    public async Task<string> SendAsync(
        IReadOnlyList<ZenithAiChatMessage> conversation,
        string audioContext,
        CancellationToken cancellationToken)
    {
        ReloadSettings();
        if (!Settings.IsConfigured)
        {
            throw new InvalidOperationException("ZenithAI no tiene API configurada. Abre Config API y guarda proveedor, modelo y API key.");
        }

        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = BuildSystemPrompt(audioContext)
            }
        };

        messages.AddRange(conversation
            .TakeLast(12)
            .Select(message => new
            {
                role = message.Role,
                content = message.Content
            }));

        var payload = BuildPayload(Settings.Model, messages);

        using var request = new HttpRequestMessage(HttpMethod.Post, NormalizeChatEndpoint(Settings.Endpoint));
        ApplyAuthHeaders(request, Settings.ApiKey, Settings.Endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{Settings.Provider} respondió {(int)response.StatusCode}: {TrimForUi(body)}");
        }

        return ExtractAssistantText(body);
    }

    /// <summary>
    /// Universal model discovery across any OpenAI-compatible provider (NVIDIA NIM, OpenAI, OpenRouter,
    /// Groq, Gemini OpenAI-compat, DeepSeek, Mistral, Together, Ollama, LM Studio, custom servers).
    /// </summary>
    public async Task<List<ZenithAiModelInfo>> FetchModelsAsync(
        string endpoint,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("Ingresa primero el endpoint del proveedor para extraer los modelos.");
        }

        var candidateUrls = BuildModelsEndpoints(endpoint);
        Exception? lastError = null;

        foreach (var modelsUrl in candidateUrls)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
                ApplyAuthHeaders(request, apiKey, modelsUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    lastError = new InvalidOperationException($"HTTP {(int)response.StatusCode} en {modelsUrl}: {TrimForUi(body)}");
                    continue;
                }

                var models = ParseModelsJson(body);
                if (models.Count > 0)
                {
                    return models;
                }

                lastError = new InvalidOperationException($"El servidor respondió en {modelsUrl} pero no devolvió modelos legibles.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new InvalidOperationException("No se pudieron extraer modelos desde el endpoint indicado.");
    }

    public async Task<string> TestConnectionAsync(
        string provider,
        string endpoint,
        string model,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("Falta el endpoint.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Selecciona o escribe un modelo antes de probar.");
        }

        if (!ZenithAiSettings.IsLocalEndpoint(endpoint) && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Falta la API key para este proveedor.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model.Trim(),
            ["messages"] = new object[]
            {
                new { role = "user", content = "Responde únicamente con: OK" }
            },
            ["max_tokens"] = 96,
            ["temperature"] = 0.0,
            ["stream"] = false
        };

        if (model.Contains("nemotron-3", StringComparison.OrdinalIgnoreCase))
        {
            payload["chat_template_kwargs"] = new Dictionary<string, object?>
            {
                ["enable_thinking"] = false
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, NormalizeChatEndpoint(endpoint));
        ApplyAuthHeaders(request, apiKey, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{provider} respondió {(int)response.StatusCode}: {TrimForUi(body)}");
        }

        var parsed = ZenithAiActionParser.Parse(ExtractAssistantText(body));
        var sample = string.IsNullOrWhiteSpace(parsed.CleanText) ? "OK" : parsed.CleanText;
        return $"Conexión exitosa con {provider} ({model}): {TrimForUi(sample)}";
    }

    public static string NormalizeChatEndpoint(string rawEndpoint)
    {
        var trimmed = rawEndpoint.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return ZenithAiSettings.DefaultEndpoint;
        }

        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed + "/chat/completions";
        }

        if (trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[..^"/models".Length] + "/chat/completions";
        }

        return trimmed;
    }

    private static List<string> BuildModelsEndpoints(string rawEndpoint)
    {
        var urls = new List<string>();
        var trimmed = rawEndpoint.Trim().TrimEnd('/');

        if (trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
        {
            urls.Add(trimmed);
            return urls;
        }

        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            urls.Add(trimmed[..^"/chat/completions".Length] + "/models");
        }
        else if (trimmed.EndsWith("/completions", StringComparison.OrdinalIgnoreCase))
        {
            urls.Add(trimmed[..^"/completions".Length] + "/models");
        }
        else if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
        {
            urls.Add(trimmed + "/models");
        }
        else
        {
            urls.Add(trimmed + "/v1/models");
            urls.Add(trimmed + "/models");
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var root = $"{uri.Scheme}://{uri.Authority}";
            if (uri.Port == 11434 || trimmed.Contains("ollama", StringComparison.OrdinalIgnoreCase))
            {
                urls.Add(root + "/api/tags");
            }
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<ZenithAiModelInfo> ParseModelsJson(string json)
    {
        var result = new List<ZenithAiModelInfo>();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("data", out var dataArray) &&
            dataArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in dataArray.EnumerateArray())
            {
                AddModelElement(item, result);
            }
        }
        else if (root.ValueKind == JsonValueKind.Object &&
                 root.TryGetProperty("models", out var modelsArray) &&
                 modelsArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in modelsArray.EnumerateArray())
            {
                AddModelElement(item, result);
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                AddModelElement(item, result);
            }
        }

        return result
            .GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddModelElement(JsonElement item, List<ZenithAiModelInfo> target)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            var id = item.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(id))
            {
                target.Add(new ZenithAiModelInfo(id, ExtractOwnerFromId(id), string.Empty));
            }

            return;
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var modelId = TryGetString(item, "id")
            ?? TryGetString(item, "name")
            ?? TryGetString(item, "model");

        if (string.IsNullOrWhiteSpace(modelId))
        {
            return;
        }

        if (modelId.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            modelId = modelId["models/".Length..];
        }

        var owner = TryGetString(item, "owned_by")
            ?? TryGetString(item, "provider")
            ?? TryGetString(item, "architecture")
            ?? ExtractOwnerFromId(modelId);

        var badge = string.Empty;
        if (item.TryGetProperty("context_length", out var ctx) && ctx.ValueKind == JsonValueKind.Number && ctx.TryGetInt64(out var ctxLen) && ctxLen > 0)
        {
            badge = ctxLen >= 1000 ? $"{ctxLen / 1000}K ctx" : $"{ctxLen} ctx";
        }
        else if (item.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object)
        {
            var paramSize = TryGetString(details, "parameter_size");
            if (!string.IsNullOrWhiteSpace(paramSize))
            {
                badge = paramSize;
            }
        }

        if (string.IsNullOrWhiteSpace(badge))
        {
            badge = owner.Length > 14 ? owner[..14] : owner;
        }

        target.Add(new ZenithAiModelInfo(modelId.Trim(), owner, badge));
    }

    private static string? TryGetString(JsonElement obj, string propertyName)
    {
        return obj.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()?.Trim()
            : null;
    }

    private static string ExtractOwnerFromId(string id)
    {
        var slash = id.IndexOf('/');
        return slash > 0 ? id[..slash] : "API";
    }

    private static void ApplyAuthHeaders(HttpRequestMessage request, string apiKey, string endpoint)
    {
        var normalizedKey = NormalizeApiKey(apiKey);
        if (!string.IsNullOrWhiteSpace(normalizedKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", normalizedKey);
        }

        if (endpoint.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/xn1llox/Zenith-Audio-Player");
            request.Headers.TryAddWithoutValidation("X-Title", "Zenith Audio Player");
        }
    }

    private static string BuildSystemPrompt(string audioContext)
    {
        return
            "Eres ZenithAI, el asistente audiófilo integrado directamente dentro de Zenith Audio Player (Windows). " +
            "Responde siempre en español claro, experto y útil. " +
            "Tu dominio es audio, música, artistas, álbumes, masterización, DSD/FLAC/PCM, DACs, WASAPI, ASIO, EQ y el control directo del reproductor Zenith Audio. " +
            "Puedes ejecutar acciones REALES en el reproductor incluyendo una o más etiquetas exactas al final de tu respuesta (serán ejecutadas automáticamente y ocultadas al usuario): " +
            "[[ZENITH_ACTION:play_track|query=nombre de canción o álbum de la biblioteca]], " +
            "[[ZENITH_ACTION:pause]], [[ZENITH_ACTION:resume]], [[ZENITH_ACTION:stop]], [[ZENITH_ACTION:next]], [[ZENITH_ACTION:previous]], " +
            "[[ZENITH_ACTION:volume|value=0-100]], [[ZENITH_ACTION:shuffle|value=on|off]], [[ZENITH_ACTION:seek|seconds=90]], " +
            "[[ZENITH_ACTION:backend|value=wasapi|asio|bass|mpv]], [[ZENITH_ACTION:output|query=nombre parcial del DAC o salida]], " +
            "[[ZENITH_ACTION:exclusive|value=on|off]], [[ZENITH_ACTION:dsd_mode|value=pcm_fir|dop|native]], " +
            "[[ZENITH_ACTION:dsd_gain|value=0|3|6]], [[ZENITH_ACTION:dsd_rate|value=0|44100|88200|176400|352800]], " +
            "[[ZENITH_ACTION:tone|eq=true|bypass=false|preamp=-2|sub=2|presence=1|air=1]] (o [[ZENITH_ACTION:tone|bypass=true]] para audio puro), " +
            "[[ZENITH_ACTION:filter|value=all|dsd|hires|folders|search:texto]], " +
            "[[ZENITH_ACTION:analyze]], [[ZENITH_ACTION:open_tab|value=signal|tone|settings|asio_panel]]. " +
            "Usa estas etiquetas SOLO cuando el usuario pida explícitamente controlar el reproductor, reproducir música, cambiar salida/motor/DSD o ajustar el sonido/EQ. " +
            "Si el usuario pide información musical o técnica sin pedir cambios, responde normalmente sin etiquetas. " +
            "No inventes pistas que no estén en el contexto de biblioteca. " +
            "Estado actual del reproductor: " + audioContext;
    }

    private static Dictionary<string, object?> BuildPayload(string model, List<object> messages)
    {
        var isNemotron3 = model.Contains("nemotron-3", StringComparison.OrdinalIgnoreCase);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = isNemotron3 ? 1.0 : 0.45,
            ["top_p"] = isNemotron3 ? 0.95 : 0.9,
            ["max_tokens"] = isNemotron3 ? 8192 : 2048,
            ["stream"] = false
        };

        if (isNemotron3)
        {
            payload["reasoning_budget"] = 4096;
            payload["chat_template_kwargs"] = new Dictionary<string, object?>
            {
                ["enable_thinking"] = true
            };
        }

        return payload;
    }

    private static string ExtractAssistantText(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            var finishReason = firstChoice.TryGetProperty("finish_reason", out var finishReasonElement) &&
                               finishReasonElement.ValueKind == JsonValueKind.String
                ? finishReasonElement.GetString()
                : null;

            if (firstChoice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                var extracted = ExtractContentElement(content);
                return AddTruncationNotice(extracted, finishReason);
            }

            if (firstChoice.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return AddTruncationNotice(text.GetString(), finishReason);
            }
        }

        return "ZenithAI recibió una respuesta sin texto legible.";
    }

    private static string? ExtractContentElement(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object &&
                    part.TryGetProperty("text", out var textPart) &&
                    textPart.ValueKind == JsonValueKind.String)
                {
                    sb.Append(textPart.GetString());
                }
            }

            return sb.ToString();
        }

        return null;
    }

    private static string TrimForUi(string value)
    {
        value = value.Trim();
        return value.Length <= 700 ? value : value[..700] + "...";
    }

    private static string AddTruncationNotice(string? value, string? finishReason)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "ZenithAI no devolvió contenido.";
        }

        return finishReason?.Equals("length", StringComparison.OrdinalIgnoreCase) == true
            ? text + "\n\n[Respuesta limitada por la API. Escribe \"continúa\" para seguir desde este punto.]"
            : text;
    }

    private static string NormalizeApiKey(string apiKey)
    {
        apiKey = apiKey.Trim();
        const string bearerPrefix = "Bearer ";
        return apiKey.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? apiKey[bearerPrefix.Length..].Trim()
            : apiKey;
    }
}
