using System.Text.Json;
using MakroChef.Domain.Catalog;

namespace MakroChef.Agent.Catalog;

/// <summary>Schema-agnostic parsing of get_product_details, same reasoning as the 3.4 probe and
/// 4.1 profiler — tools/list gives input schemas, not response shapes, so field names are
/// best-effort until a live account (BLOCKERS.md B-2) shows the real payload.</summary>
public static class ProductDetailsParser
{
    private static readonly string[] PriceKeys = ["priceKopecks", "price", "priceAfterDiscount", "discountedPrice"];
    private static readonly string[] PromotionKeys = ["onPromotion", "isPromotion", "hasDiscount"];
    private static readonly string[] BarcodeKeys = ["barcode", "ean", "gtin"];
    private static readonly string[] WeightKeys = ["weightGrams", "weight", "netWeight", "packageWeight"];
    private const decimal DefaultWeightGrams = 100m; // TASKS.md doesn't guarantee this field exists; 100g keeps per-100g nutrients usable as a per-unit estimate until a live payload proves otherwise.

    public static ProductDetails Parse(string productId, string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new ProductDetails(productId, "невідома", 0, false, null, DefaultWeightGrams);
        }

        var category = root.TryGetProperty("category", out var categoryValue) && categoryValue.ValueKind == JsonValueKind.String
            ? categoryValue.GetString()!
            : "невідома";

        var price = ReadPriceKopecks(root);
        var onPromotion = ReadBool(root, PromotionKeys);
        var barcode = ReadString(root, BarcodeKeys);
        var weight = ReadNumber(root, WeightKeys) ?? DefaultWeightGrams;

        return new ProductDetails(productId, category, price, onPromotion, barcode, weight);
    }

    private static decimal? ReadNumber(JsonElement root, string[] keyCandidates)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (keyCandidates.Contains(prop.Name, StringComparer.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Number)
            {
                return prop.Value.GetDecimal();
            }
        }

        return null;
    }

    private static long ReadPriceKopecks(JsonElement root)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (PriceKeys.Contains(prop.Name, StringComparer.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Number)
            {
                // Prices come back as decimal currency (e.g. 42.50); solver needs integer kopecks.
                return (long)Math.Round(prop.Value.GetDecimal() * 100);
            }
        }

        return 0;
    }

    private static bool ReadBool(JsonElement root, string[] keyCandidates)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (keyCandidates.Contains(prop.Name, StringComparer.OrdinalIgnoreCase) && prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return prop.Value.GetBoolean();
            }
        }

        return false;
    }

    private static string? ReadString(JsonElement root, string[] keyCandidates)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (keyCandidates.Contains(prop.Name, StringComparer.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
            {
                return prop.Value.GetString();
            }
        }

        return null;
    }
}
