using System.Globalization;
using System.Text.Json;
using MakroChef.Domain.Cart;
using MakroChef.Domain.Nutrition;
using MakroChef.Mcp;

namespace MakroChef.Nutrition;

/// <summary>Path A (TASKS.md 4.0): coverage >= 60% -> trust get_product_details directly.
///
/// Confirmed live (2026-09-14): silpo_get_product_details takes branchId + deliveryType +
/// timeslotStart + timeslotEnd + slug — NOT productId. The slug can only come from a prior
/// silpo_find_products_batch/get_products result (JsonFieldScanner.ExtractProductSlugs), never
/// guessed from a name. <see cref="ResolveAsync"/>'s first parameter is therefore the SLUG, not
/// the product's own id — callers must resolve that first.
///
/// Nutrients live in product.attributes under Ukrainian food-label keys ("Білки (г)", "Жири
/// (г)", "Вуглеводи (г)"), not English ones tools/list could never have revealed, and energy
/// comes as a combined "kcal/kJ" string like "241/1013".</summary>
public class ExactMcpNutritionResolver(IMakroChefMcpClient mcpClient, SessionContext session) : INutritionResolver
{
    public async Task<NutrientInfo?> ResolveAsync(string slug, string? barcode, CancellationToken cancellationToken = default)
    {
        var json = await mcpClient.CallToolAsync(
            "silpo_get_product_details",
            new Dictionary<string, object?>
            {
                ["branchId"] = session.BranchId,
                ["deliveryType"] = session.DeliveryType,
                ["timeslotStart"] = session.TimeslotStart,
                ["timeslotEnd"] = session.TimeslotEnd,
                ["slug"] = slug,
            },
            cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var (protein, fat, carbs, sugar, kcal) = ParseNutrients(json);
        if (protein is null && fat is null && carbs is null && sugar is null && kcal is null)
        {
            return null;
        }

        return new NutrientInfo(protein, fat, carbs, sugar, kcal, Source: "mcp");
    }

    public static (decimal? Protein, decimal? Fat, decimal? Carbs, decimal? Sugar, decimal? Kcal) ParseNutrients(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        var product = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("product", out var p) && p.ValueKind == JsonValueKind.Object
            ? p
            : root;

        if (product.ValueKind != JsonValueKind.Object || !product.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
        {
            return (null, null, null, null, null);
        }

        var protein = ReadAttributeNumber(attributes, "Білки");
        var fat = ReadAttributeNumber(attributes, "Жири");
        var carbs = ReadAttributeNumber(attributes, "Вуглеводи");
        var sugar = ReadAttributeNumber(attributes, "цукри"); // best guess: "У тому числі цукри (г)" is the common label pattern, unconfirmed live
        var kcal = ParseKcalFromEnergyLabel(ReadAttributeString(attributes, "Енергетична цінність"));

        return (protein, fat, carbs, sugar, kcal);
    }

    private static decimal? ReadAttributeNumber(JsonElement attributes, string keyContains)
    {
        foreach (var prop in attributes.EnumerateObject())
        {
            if (!prop.Name.Contains(keyContains, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.Number)
            {
                return prop.Value.GetDecimal();
            }

            if (prop.Value.ValueKind == JsonValueKind.String && decimal.TryParse(prop.Value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? ReadAttributeString(JsonElement attributes, string keyContains)
    {
        foreach (var prop in attributes.EnumerateObject())
        {
            if (prop.Name.Contains(keyContains, StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.String)
            {
                return prop.Value.GetString();
            }
        }

        return null;
    }

    private static decimal? ParseKcalFromEnergyLabel(string? label)
    {
        if (label is null)
        {
            return null;
        }

        var kcalPart = label.Split('/')[0].Trim();
        return decimal.TryParse(kcalPart, NumberStyles.Any, CultureInfo.InvariantCulture, out var kcal) ? kcal : null;
    }
}
