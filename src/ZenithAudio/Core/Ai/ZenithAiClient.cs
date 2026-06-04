using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ZenithAudio.Core.Ai;

public sealed class ZenithAiClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

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
            throw new InvalidOperationException("ZenithAI no tiene API configurada. Abre Config en ZenithAI y agrega una API key.");
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

        var payload = new
        {
            model = Settings.Model,
            messages,
            temperature = 0.45,
            top_p = 0.9,
            max_tokens = 1400,
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Settings.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NormalizeApiKey(Settings.ApiKey));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{Settings.Provider} respondio {(int)response.StatusCode}: {TrimForUi(body)}");
        }

        return ExtractAssistantText(body);
    }

    private static string BuildSystemPrompt(string audioContext)
    {
        return
            "Eres ZenithAI (BETA), un asistente integrado en Zenith Audio. " +
            "Responde siempre en espanol claro, experto y completo cuando el usuario pida analisis tecnico o musical. " +
            "Tu dominio es exclusivamente audio: historia de la musica, artistas, albumes, formatos, DSD, FLAC, PCM, DACs, WASAPI, MPV, BASS, masterizacion, escucha critica, configuracion de Windows y buenas practicas audiofilas. " +
            "Usa el contexto de la pista actual solo como referencia. No puedes leer carpetas ni discos directamente. " +
            "Si el usuario pregunta algo fuera de audio, redirige amablemente al tema musical o de reproduccion. " +
            "No inventes datos tecnicos; cuando no sepas algo, dilo y ofrece una forma de comprobarlo. Evita cortar frases o listas a medias. " +
            "Contexto actual del reproductor: " + audioContext;
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
            var finishReason = firstChoice.TryGetProperty("finish_reason", out var finishReasonElement)
                ? finishReasonElement.GetString()
                : null;

            if (firstChoice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                return AddTruncationNotice(content.GetString(), finishReason);
            }

            if (firstChoice.TryGetProperty("text", out var text))
            {
                return AddTruncationNotice(text.GetString(), finishReason);
            }
        }

        return "ZenithAI recibio una respuesta sin texto legible.";
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
            return "ZenithAI no devolvio contenido.";
        }

        return finishReason?.Equals("length", StringComparison.OrdinalIgnoreCase) == true
            ? text + "\n\n[Respuesta limitada por la API. Escribe \"continua\" para seguir desde este punto.]"
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
