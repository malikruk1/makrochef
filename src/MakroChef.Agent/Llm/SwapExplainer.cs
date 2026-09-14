using System.Text;
using System.Text.Json;
using MakroChef.Domain.Catalog;

namespace MakroChef.Agent.Llm;

/// <summary>An explained swap: the solver-computed <see cref="ProductSwap"/> plus one short,
/// human-readable sentence. TASKS.md 0.2: "LLM ... пояснює свопи" - the LLM only ever narrates a
/// decision the solver already made from real deltas; it never picks or scores the swap itself.</summary>
public record ExplainedSwap(ProductSwap Swap, string Explanation);

/// <summary>Batches all of a basket's swaps into one Claude call (cheaper and faster than one
/// call per swap) and asks for a short explanation of each. Falls back to a deterministic
/// template sentence - built purely from the same numbers already in ProductSwap - whenever no
/// LLM client is configured or the call fails for any reason, so screen 3 always has something
/// sensible to show.</summary>
public class SwapExplainer(IAnthropicClient llmClient)
{
    private const string SystemPrompt =
        "Ти пояснюєш заміни товарів у кошику покупок гостя Сільпо українською мовою. " +
        "Тобі дають список замін з РЕАЛЬНИМИ цифрами (вже пораховані, не змінюй їх і не вигадуй нові). " +
        "Для кожної заміни напиши ОДНЕ коротке речення (до 12 слів), природною розмовною мовою, " +
        "чому ця заміна варта уваги - без зайвих слів, без markdown, без лапок. " +
        "Поверни ТІЛЬКИ JSON-масив рядків, той самий порядок і довжина, що і вхідний список. " +
        "Нічого крім JSON-масиву.";

    public async Task<IReadOnlyList<ExplainedSwap>> ExplainAsync(IReadOnlyList<ProductSwap> swaps, CancellationToken cancellationToken = default)
    {
        if (swaps.Count == 0)
        {
            return [];
        }

        var explanations = await TryGetLlmExplanationsAsync(swaps, cancellationToken) ?? swaps.Select(FallbackExplanation).ToList();

        // Defensive: never let a malformed/short LLM response desync explanations from swaps -
        // fall back to the deterministic template for anything missing.
        var result = new List<ExplainedSwap>(swaps.Count);
        for (var i = 0; i < swaps.Count; i++)
        {
            var explanation = i < explanations.Count && !string.IsNullOrWhiteSpace(explanations[i])
                ? explanations[i]
                : FallbackExplanation(swaps[i]);
            result.Add(new ExplainedSwap(swaps[i], explanation));
        }

        return result;
    }

    private async Task<List<string>?> TryGetLlmExplanationsAsync(IReadOnlyList<ProductSwap> swaps, CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(swaps);
        var response = await llmClient.CompleteAsync(SystemPrompt, prompt, cancellationToken);
        if (response is null)
        {
            return null;
        }

        try
        {
            var json = ExtractJsonArray(response);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return doc.RootElement.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "")
                .ToList();
        }
        catch (JsonException)
        {
            // The model can wrap the array in prose despite instructions not to - treat any
            // unparseable response as "no LLM explanation available", not an error.
            return null;
        }
    }

    /// <summary>Claude sometimes wraps the requested JSON in a code fence or a lead-in sentence
    /// despite instructions - take the substring between the first '[' and the last ']'.</summary>
    private static string ExtractJsonArray(string response)
    {
        var start = response.IndexOf('[');
        var end = response.LastIndexOf(']');
        return start >= 0 && end > start ? response[start..(end + 1)] : response;
    }

    private static string BuildPrompt(IReadOnlyList<ProductSwap> swaps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[");
        for (var i = 0; i < swaps.Count; i++)
        {
            var s = swaps[i];
            sb.Append("  {\"було\":\"").Append(s.OldName ?? s.OldProductId)
              .Append("\",\"стало\":\"").Append(s.NewName ?? s.NewProductId)
              .Append("\",\"білокГ\":").Append(s.ProteinDeltaGrams)
              .Append(",\"цукорГ\":").Append(s.SugarDeltaGrams)
              .Append(",\"цінаКоп\":").Append(s.PriceDeltaKopecks)
              .Append(",\"акція\":").Append(s.OnPromotion ? "true" : "false")
              .Append('}')
              .Append(i < swaps.Count - 1 ? "," : "")
              .AppendLine();
        }

        sb.AppendLine("]");
        return sb.ToString();
    }

    /// <summary>No invented claims - built purely from ProductSwap's own numbers, same as the
    /// mock UI's existing "+9 г білка, −11 г цукру, −3 ₴" style line.</summary>
    private static string FallbackExplanation(ProductSwap swap)
    {
        var parts = new List<string>();
        if (swap.ProteinDeltaGrams > 0)
        {
            parts.Add($"+{swap.ProteinDeltaGrams:F0} г білка");
        }

        if (swap.SugarDeltaGrams < 0)
        {
            parts.Add($"{swap.SugarDeltaGrams:F0} г цукру");
        }

        if (swap.PriceDeltaKopecks != 0)
        {
            var sign = swap.PriceDeltaKopecks > 0 ? "+" : "";
            parts.Add($"{sign}{swap.PriceDeltaKopecks / 100m:F2} ₴");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "Покращення без суттєвої зміни ціни.";
    }
}
