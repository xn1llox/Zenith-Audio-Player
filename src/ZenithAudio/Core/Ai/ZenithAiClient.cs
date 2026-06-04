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
            max_tokens = 520,
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Settings.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.ApiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
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
            "Responde siempre en espanol claro, breve y experto. " +
            "Tu dominio es exclusivamente audio: historia de la musica, artistas, albumes, formatos, DSD, FLAC, PCM, DACs, WASAPI, MPV, BASS, masterizacion, escucha critica, configuracion de Windows y buenas practicas audiofilas. " +
            "Usa el contexto de la pista actual solo como referencia. No puedes leer carpetas ni discos directamente. " +
            "Si el usuario pregunta algo fuera de audio, redirige amablemente al tema musical o de reproduccion. " +
            "No inventes datos tecnicos; cuando no sepas algo, dilo y ofrece una forma de comprobarlo. " +
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
            if (firstChoice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                return content.GetString()?.Trim() ?? "ZenithAI no devolvio contenido.";
            }

            if (firstChoice.TryGetProperty("text", out var text))
            {
                return text.GetString()?.Trim() ?? "ZenithAI no devolvio contenido.";
            }
        }

        return "ZenithAI recibio una respuesta sin texto legible.";
    }

    private static string TrimForUi(string value)
    {
        value = value.Trim();
        return value.Length <= 700 ? value : value[..700] + "...";
    }
}
