using System.Text.Json;

namespace MakroChef.Agent.Coverage;

/// <summary>Best-effort extraction from MCP tool-call JSON payloads whose exact shape we can't
/// pin down yet — tools/list gives input schemas, not output ones, and we won't see a real
/// get_my_offline_orders/get_product_details response until B-2 is lifted. Scans recursively
/// for known field-name candidates (case-insensitive) instead of assuming one fixed nesting.</summary>
public static class JsonFieldScanner
{
    private static readonly string[] SkuKeyCandidates = ["productId", "sku", "itemId", "product_id", "id"];
    // "sumReg" confirmed live (2026-09-14): silpo_get_my_offline_orders' per-order total, not
    // "totalAmount"/"total"/"sum"/"amount" as guessed - silpo_get_my_online_orders does use "amount".
    private static readonly string[] AmountKeyCandidates = ["totalAmount", "total", "sum", "amount", "sumReg"];
    private static readonly string[] DateKeyCandidates = ["createdAt", "date", "orderDate", "created_at"];
    // Confirmed live: both silpo_get_my_offline_orders and silpo_get_my_online_orders wrap their
    // per-order line items in a "products" array, not "items".
    private static readonly string[] LineItemsKeyCandidates = ["items", "products"];

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

    /// <summary>One (date, productId, quantity) row per line item across all orders, best-effort
    /// — used by week-over-week analysis (TASKS.md, screen 6) to group actual purchases by
    /// calendar week. Quantity defaults to 1 when the field is missing/unconfirmed live.</summary>
    public static IReadOnlyList<(DateTimeOffset Date, string ProductId, int Quantity)> ExtractOrderItems(string json)
    {
        var results = new List<(DateTimeOffset, string, int)>();
        var root = Parse(json);

        foreach (var order in EnumerateOrderLikeObjects(root))
        {
            DateTimeOffset? date = null;
            foreach (var prop in order.EnumerateObject())
            {
                if (date is null && DateKeyCandidates.Contains(prop.Name, StringComparer.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(prop.Value.GetString(), out var parsed))
                {
                    date = parsed;
                }
            }

            JsonElement items = default;
            var foundItems = false;
            foreach (var key in LineItemsKeyCandidates)
            {
                if (order.TryGetProperty(key, out items) && items.ValueKind == JsonValueKind.Array)
                {
                    foundItems = true;
                    break;
                }
            }

            if (date is null || !foundItems)
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? productId = null;
                foreach (var key in SkuKeyCandidates)
                {
                    if (item.TryGetProperty(key, out var idValue) && idValue.ValueKind == JsonValueKind.String)
                    {
                        productId = idValue.GetString();
                        break;
                    }
                }

                if (productId is null)
                {
                    continue;
                }

                // Confirmed live (2026-09-14): real order quantities can be fractional for
                // weighed goods (e.g. "quantity":0.466 for bananas by kg) - GetInt32() throws
                // FormatException on those. Read as decimal and round, with a floor of 1 so a
                // weighed item still contributes ~one unit-equivalent instead of silently
                // zeroing out (consistent with this analyzer's documented ~100g approximation).
                var quantity = 1;
                foreach (var key in new[] { "quantity", "qty", "count" })
                {
                    if (item.TryGetProperty(key, out var qtyValue) && qtyValue.ValueKind == JsonValueKind.Number)
                    {
                        quantity = Math.Max(1, (int)Math.Round(qtyValue.GetDecimal()));
                        break;
                    }
                }

                results.Add((date.Value, productId, quantity));
            }
        }

        return results;
    }

    /// <summary>Confirmed live (2026-09-14): silpo_get_my_offline_orders/silpo_get_my_online_orders
    /// both wrap the actual order list in a root-level "orders" array
    /// (<c>{"success":true,"summary":"...","orders":[...]}</c>), not a bare top-level array or a
    /// single order object as originally guessed - without unwrapping this, every order's amount
    /// and date lived one level too deep to ever be found, silently producing a null median
    /// weekly receipt (BLOCKERS.md #17).</summary>
    private static IEnumerable<JsonElement> EnumerateOrderLikeObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("orders", out var orders) && orders.ValueKind == JsonValueKind.Array)
        {
            element = orders;
        }

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

    /// <summary>Confirmed live (2026-09-14): a catalog tool can return a plain-text MCP error
    /// (e.g. a batch-size-limit message) instead of JSON even when the call itself succeeds at
    /// the transport level. Every scanner here just wants "extract what you can" from a
    /// best-effort payload, so an unparseable response yields nothing found rather than an
    /// unhandled crash — the same tolerance already given to a single missing field.</summary>
    private static JsonElement Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException)
        {
            return default;
        }
    }

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
