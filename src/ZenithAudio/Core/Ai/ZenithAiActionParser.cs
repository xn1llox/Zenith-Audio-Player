using System.Text.RegularExpressions;

namespace ZenithAudio.Core.Ai;

public sealed record ZenithAiAction(string Name, IReadOnlyDictionary<string, string> Parameters);

public sealed record ZenithAiParsedResponse(string CleanText, IReadOnlyList<ZenithAiAction> Actions);

public static partial class ZenithAiActionParser
{
    [GeneratedRegex(@"\[\[ZENITH_ACTION:([^\]]+)\]\]", RegexOptions.IgnoreCase)]
    private static partial Regex ActionTagRegex();

    [GeneratedRegex(@"<think>[\s\S]*?</think>", RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlockRegex();

    public static ZenithAiParsedResponse Parse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return new ZenithAiParsedResponse(string.Empty, []);
        }

        var withoutThinking = ThinkBlockRegex().Replace(rawResponse, string.Empty);
        var actions = new List<ZenithAiAction>();

        foreach (Match match in ActionTagRegex().Matches(withoutThinking))
        {
            var body = match.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            var parts = body.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var actionName = parts[0].ToLowerInvariant();
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 1; i < parts.Length; i++)
            {
                var segment = parts[i];
                var eqIndex = segment.IndexOf('=');
                if (eqIndex > 0)
                {
                    var key = segment[..eqIndex].Trim();
                    var value = segment[(eqIndex + 1)..].Trim();
                    parameters[key] = value;
                }
                else
                {
                    parameters["value"] = segment;
                }
            }

            actions.Add(new ZenithAiAction(actionName, parameters));
        }

        var cleanText = ActionTagRegex().Replace(withoutThinking, string.Empty).Trim();
        return new ZenithAiParsedResponse(cleanText, actions);
    }
}
