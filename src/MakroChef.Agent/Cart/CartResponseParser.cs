using System.Text.Json;
using MakroChef.Domain.Cart;

namespace MakroChef.Agent.Cart;

/// <summary>Parsing of get_shopping_cart_by_id. Confirmed against a live cart (2026-09): the
/// real response nests everything under "cart" (cart.shipments[].products[], cart.calculation.
/// validations[], cart.calculation.totalAfterDiscounts) — scans recursively through any depth of
/// nesting instead of assuming one fixed shape, so this also still matches the stub fixtures'
/// flatter shape used by the gate 7.1/7.2 tests.</summary>
public static class CartResponseParser
{
    public static CartState Parse(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new CartState([], [], 0);
        }

        var lines = new List<CartLine>();
        var validations = new List<CartValidationIssue>();
        long? totalKopecks = null;

        CollectLinesAndValidations(root, lines, validations, ref totalKopecks);

        return new CartState(lines, validations, totalKopecks ?? 0);
    }

    private static void CollectLinesAndValidations(
        JsonElement element, List<CartLine> lines, List<CartValidationIssue> validations, ref long? totalKopecks)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    TryParseAsLine(item, lines);
                    TryParseAsValidation(item, validations);
                    CollectLinesAndValidations(item, lines, validations, ref totalKopecks);
                }

                break;

            case JsonValueKind.Object:
                // Prefer totalAfterDiscounts (what the guest actually pays) over a plain "total".
                if (totalKopecks is null)
                {
                    var preferred = ReadNumber(element, ["totalAfterDiscounts"]);
                    if (preferred is not null)
                    {
                        totalKopecks = (long)Math.Round(preferred.Value * 100);
                    }
                }

                foreach (var prop in element.EnumerateObject())
                {
                    CollectLinesAndValidations(prop.Value, lines, validations, ref totalKopecks);
                }

                if (totalKopecks is null)
                {
                    var fallback = ReadNumber(element, ["total", "totalAmount", "sum"]);
                    if (fallback is not null)
                    {
                        totalKopecks = (long)Math.Round(fallback.Value * 100);
                    }
                }

                break;
        }
    }

    private static void TryParseAsLine(JsonElement item, List<CartLine> lines)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var productId = ReadString(item, ["productId"]);
        // Require an explicit quantity — a validation entry can also carry a bare "productId"
        // (in its "context", or flat in stub fixtures) without being a real cart line.
        var quantity = ReadNumber(item, ["quantity", "qty", "units"]);
        if (productId is null || quantity is null)
        {
            return;
        }

        lines.Add(new CartLine(productId, (int)Math.Round(quantity.Value)));
    }

    private static void TryParseAsValidation(JsonElement item, List<CartValidationIssue> validations)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // Real shape: {"level":"error","type":"product","message":"product.offer.status.not_available","context":{"productId":"..."}}
        var message = ReadString(item, ["message", "reason", "status", "code"]);
        if (message is null)
        {
            return;
        }

        var productId = ReadString(item, ["productId", "sku", "id"]);
        if (productId is null && item.TryGetProperty("context", out var context) && context.ValueKind == JsonValueKind.Object)
        {
            productId = ReadString(context, ["productId", "sku", "id"]);
        }

        validations.Add(new CartValidationIssue(productId ?? "", message));
    }

    private static string? ReadString(JsonElement element, string[] keyCandidates)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (keyCandidates.Contains(prop.Name, StringComparer.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
            {
                return prop.Value.GetString();
            }
        }

        return null;
    }

    private static decimal? ReadNumber(JsonElement element, string[] keyCandidates)
    {
        foreach (var prop in element.EnumerateObject())
        {
            if (keyCandidates.Contains(prop.Name, StringComparer.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Number)
            {
                return prop.Value.GetDecimal();
            }
        }

        return null;
    }
}
