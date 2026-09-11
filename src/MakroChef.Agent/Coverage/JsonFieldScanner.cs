using System.Text.Json;

namespace MakroChef.Agent.Coverage;

/// <summary>Best-effort extraction from MCP tool-call JSON payloads whose exact shape we can't
/// pin down yet — tools/list gives input schemas, not output ones, and we won't see a real
/// get_my_offline_orders/get_product_details response until B-2 is lifted. Scans recursively
/// for known field-name candidates (case-insensitive) instead of assuming one fixed nesting.</summary>
public static class JsonFieldScanner
{
    private static readonly string[] SkuKeyCandidates = ["productId", "sku", "itemId", "product_id"];
    private static readonly string[] AmountKeyCandidates = ["totalAmount", "total", "sum", "amount"];
    private static readonly string[] DateKeyCandidates = ["createdAt", "date", "orderDate", "created_at"];

    public static IReadOnlySet<string> ExtractProductIds(string json)
    {
        var ids = new HashSet<string>();
        WalkStrings(Parse(json), SkuKeyCandidates, s => ids.Add(s));
        return ids;
    }

    /// <summary>One (amount, date) pair per top-level order object found, best-effort.</summary>
    public static IReadOnlyList<(decimal Amount, DateTimeOffset Date)> ExtractOrderTotals(string json)
    {
        var results = new List<(decimal, DateTimeOffset)>();
        var root = Parse(json);
        foreach (var order in EnumerateOrderLikeObjects(root))
        {
            decimal? amount = null;
            DateTimeOffset? date = null;

            foreach (var prop in order.EnumerateObject())
            {
                if (amount is null && AmountKeyCandidates.Contains(prop.Name, StringComparer.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.Number)
                {
                    amount = prop.Value.GetDecimal();
                }

                if (date is null && DateKeyCandidates.Contains(prop.Name, StringComparer.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(prop.Value.GetString(), out var parsed))
                {
                    date = parsed;
                }
            }

            if (amount is not null && date is not null)
            {
                results.Add((amount.Value, date.Value));
            }
        }

        return results;
    }

    private static IEnumerable<JsonElement> EnumerateOrderLikeObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
        }
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static void WalkStrings(JsonElement element, string[] keyCandidates, Action<string> onMatch)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if (keyCandidates.Contains(prop.Name, StringComparer.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        onMatch(prop.Value.GetString()!);
                    }

                    WalkStrings(prop.Value, keyCandidates, onMatch);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    WalkStrings(item, keyCandidates, onMatch);
                }

                break;
        }
    }
}
