using System.Text.Json;
using MakroChef.Domain.Catalog;

namespace MakroChef.Agent.Catalog;

/// <summary>Parsing of silpo_get_product_details. Confirmed against a live product
/// (2026-09-14): the response wraps everything under "product" — price, stock, slug,
/// displayRatio (e.g. "500г", "900г" — the real source of weight, not a dedicated weight
/// field), and nutrients live in "attributes" under Ukrainian food-label keys, not English
/// ones tools/list could never have revealed.</summary>
public static class ProductDetailsParser
{
    private static readonly string[] PriceKeys = ["price", "priceKopecks", "priceAfterDiscount", "discountedPrice"];
    private static readonly string[] PromotionKeys = ["onPromotion", "isPromotion", "hasDiscount"];
    private static readonly string[] BarcodeKeys = ["barcode", "ean", "gtin"];
    private static readonly string[] NameKeys = ["name", "title"];
    private static readonly string[] CompanyIdKeys = ["companyId"];
    private const decimal DefaultWeightGrams = 100m; // fallback when displayRatio isn't a parseable "<number>г" (e.g. "10 шт")

    public static ProductDetails Parse(string productId, string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        var product = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("product", out var p) && p.ValueKind == JsonValueKind.Object
            ? p
            : root;

        if (product.ValueKind != JsonValueKind.Object)
        {
            return new ProductDetails(productId, "невідома", 0, false, null, DefaultWeightGrams);
        }

        var category = ReadCategory(product);
        var price = ReadPriceKopecks(product);
        var onPromotion = ReadBool(product, PromotionKeys) || HasDiscount(product);
        var barcode = ReadString(product, BarcodeKeys);
        var weight = ParseWeightFromDisplayRatio(ReadString(product, ["displayRatio", "ratio"])) ?? DefaultWeightGrams;
        var name = ReadString(product, NameKeys);
        var companyId = ReadString(product, CompanyIdKeys);

        return new ProductDetails(productId, category, price, onPromotion, barcode, weight, name, companyId);
    }

    /// <summary>"500г" -> 500, "900г" -> 900, "10 шт" -> not parseable (returns null, caller falls
    /// back to 100g) since count-based units don't carry a gram weight at all.</summary>
    private static decimal? ParseWeightFromDisplayRatio(string? displayRatio)
    {
        if (displayRatio is null)
        {
            return null;
        }

        var digits = new string(displayRatio.TakeWhile(c => char.IsDigit(c) || c == '.' || c == ',').ToArray()).Replace(',', '.');
        return displayRatio.Contains('г') && decimal.TryParse(digits, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var grams)
            ? grams
            : null;
    }

    private static string ReadCategory(JsonElement product)
    {
        if (product.TryGetProperty("category", out var categoryValue) && categoryValue.ValueKind == JsonValueKind.String)
        {
            return categoryValue.GetString()!;
        }

        return "невідома"; // real payload has no category field at all (2026-09-14 sample) - needs get_categories_tree cross-reference, not yet wired
    }

    private static bool HasDiscount(JsonElement product) =>
        product.TryGetProperty("oldPrice", out var oldPrice) && oldPrice.ValueKind == JsonValueKind.Number;

    private static long ReadPriceKopecks(JsonElement root)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (PriceKeys.Contains(prop.Name, StringComparer.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Number)
            {
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
