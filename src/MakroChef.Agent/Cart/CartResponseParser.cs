using System.Text.Json;
using MakroChef.Domain.Cart;

namespace MakroChef.Agent.Cart;

/// <summary>Schema-agnostic parsing of get_shopping_cart_by_id — same reasoning as every other
/// parser in this codebase: tools/list only gives input schemas, real response shapes are
/// unknown until BLOCKERS.md B-2 is lifted.</summary>
public static class CartResponseParser
{
    public static CartState Parse(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new CartState([], [], 0);
        }

        var lines = ParseLines(root);
        var validations = ParseValidations(root);
        var total = ReadTotalKopecks(root);

        return new CartState(lines, validations, total);
    }

    private static List<CartLine> ParseLines(JsonElement root)
    {
        var lines = new List<CartLine>();
        if (!TryGetArray(root, ["items", "lines", "products"], out var items))
        {
            return lines;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var productId = ReadString(item, ["productId", "sku", "id"]);
            var quantity = (int)(ReadNumber(item, ["quantity", "qty", "units"]) ?? 1);

            if (productId is not null)
            {
                lines.Add(new CartLine(productId, quantity));
            }
        }

        return lines;
    }

    private static List<CartValidationIssue> ParseValidations(JsonElement root)
    {
        var issues = new List<CartValidationIssue>();
        if (!TryGetArray(root, ["validations", "issues", "errors"], out var validations))
        {
            return issues;
        }

        foreach (var item in validations.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var productId = ReadString(item, ["productId", "sku", "id"]) ?? "";
            var reason = ReadString(item, ["reason", "message", "status", "code"]) ?? "";
            issues.Add(new CartValidationIssue(productId, reason));
        }

        return issues;
    }

    private static long ReadTotalKopecks(JsonElement root)
    {
        var total = ReadNumber(root, ["totalAmount", "total", "sum"]);
        return total is null ? 0 : (long)Math.Round(total.Value * 100);
    }

    private static bool TryGetArray(JsonElement root, string[] keyCandidates, out JsonElement array)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (keyCandidates.Contains(prop.Name, StringComparer.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
            {
                array = prop.Value;
                return true;
            }
        }

        array = default;
        return false;
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
