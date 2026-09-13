using System.Text.Json;

namespace MakroChef.Agent.Coverage;

/// <summary>Best-effort extraction from MCP tool-call JSON payloads whose exact shape we can't
/// pin down yet — tools/list gives input schemas, not output ones, and we won't see a real
/// get_my_offline_orders/get_product_details response until B-2 is lifted. Scans recursively
/// for known field-name candidates (case-insensitive) instead of assuming one fixed nesting.</summary>
public static class JsonFieldScanner
{
    private static readonly string[] SkuKeyCandidates = ["productId", "sku", "itemId", "product_id", "id"];
    private static readonly string[] AmountKeyCandidates = ["totalAmount", "total", "sum", "amount"];
    private static readonly string[] DateKeyCandidates = ["createdAt", "date", "orderDate", "created_at"];

    public static IReadOnlySet<string> ExtractProductIds(string json)
    {
        var ids = new HashSet<string>();
        WalkStrings(Parse(json), SkuKeyCandidates, s => ids.Add(s));
        return ids;
    }

    /// <summary>silpo_get_product_details needs a slug, not a productId (confirmed live,
    /// 2026-09-14) — the slug only ever appears alongside "id"/"productId" in catalog-returning
    /// tools (silpo_find_products_batch, silpo_get_products, silpo_get_similar_products,
    /// silpo_get_replacements). Scans for objects carrying both, keyed by id.</summary>
    public static IReadOnlyDictionary<string, string> ExtractProductSlugs(string json)
    {
        var result = new Dictionary<string, string>();
        WalkProductSlugPairs(Parse(json), result);
        return result;
    }

    private static void WalkProductSlugPairs(JsonElement element, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                string? id = null;
                string? slug = null;
                foreach (var prop in element.EnumerateObject())
                {
                    if (id is null && SkuKeyCandidates.Contains(prop.Name, StringComparer.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        id = prop.Value.GetString();
                    }

                    if (slug is null && prop.Name.Equals("slug", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
                    {
                        slug = prop.Value.GetString();
                    }

                    WalkProductSlugPairs(prop.Value, result);
                }

                if (id is not null && slug is not null)
                {
                    result[id] = slug;
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    WalkProductSlugPairs(item, result);
                }

                break;
        }
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
