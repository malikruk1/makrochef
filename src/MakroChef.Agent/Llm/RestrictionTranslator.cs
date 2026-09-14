using System.Text.Json;

namespace MakroChef.Agent.Llm;

/// <summary>TASKS.md 0.2: the third and last LLM job - "перекладає «без риби» у зміну обмежень".
/// Takes a guest's free-text phrase and returns a structured list of restriction categories to
/// add. Never invents a category outside common Ukrainian grocery restriction words, and never
/// touches candidate selection itself - the solver still filters on the resulting strings exactly
/// like it filters on restrictions read from MCP.</summary>
public class RestrictionTranslator(IAnthropicClient llmClient)
{
    private const string SystemPrompt =
        "Ти перекладаєш вільний текст гостя про харчові обмеження в список категорій українською " +
        "мовою (наприклад, «риба», «молочні продукти», «горіхи», «глютен», «яйця», «м'ясо», «цукор»). " +
        "Не вигадуй товари чи категорії, яких немає в тексті. Якщо текст не описує харчове обмеження, " +
        "поверни порожній масив. Поверни ТІЛЬКИ JSON-масив рядків, без пояснень і без markdown.";

    private static readonly (string Pattern, string Category)[] FallbackKeywords =
    [
        ("риб", "риба"),
        ("молок", "молочні продукти"),
        ("молочн", "молочні продукти"),
        ("горіх", "горіхи"),
        ("глютен", "глютен"),
        ("яй", "яйця"),
        ("м'яс", "м'ясо"),
        ("м’яс", "м'ясо"),
        ("цукор", "цукор"),
        ("морепродукт", "морепродукти"),
    ];

    public async Task<IReadOnlyList<string>> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var llmResult = await TryGetLlmRestrictionsAsync(text, cancellationToken);
        return llmResult ?? FallbackTranslate(text);
    }

    private async Task<List<string>?> TryGetLlmRestrictionsAsync(string text, CancellationToken cancellationToken)
    {
        var response = await llmClient.CompleteAsync(SystemPrompt, text, cancellationToken);
        if (response is null)
        {
            return null;
        }

        try
        {
            var jsonPart = ExtractJsonArray(response);
            var parsed = JsonSerializer.Deserialize<List<string>>(jsonPart);
            return parsed?.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractJsonArray(string response)
    {
        var start = response.IndexOf('[');
        var end = response.LastIndexOf(']');
        return start >= 0 && end > start ? response[start..(end + 1)] : response;
    }

    /// <summary>Deterministic keyword matcher used whenever ANTHROPIC_API_KEY is unset or the LLM
    /// call fails - a small, honest subset of phrases rather than pretending to understand
    /// arbitrary free text.</summary>
    private static List<string> FallbackTranslate(string text)
    {
        var lowered = text.ToLowerInvariant();
        return FallbackKeywords
            .Where(k => lowered.Contains(k.Pattern))
            .Select(k => k.Category)
            .Distinct()
            .ToList();
    }
}
