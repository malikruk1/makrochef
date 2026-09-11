using System.Text.Json;
using MakroChef.Domain.Nutrition;
using MakroChef.Mcp;

namespace MakroChef.Nutrition;

/// <summary>Path A (TASKS.md 4.0): coverage >= 60% -> trust get_product_details directly.</summary>
public class ExactMcpNutritionResolver(IMakroChefMcpClient mcpClient) : INutritionResolver
{
    private static readonly string[] ProteinKeys = ["proteinPer100g", "protein"];
    private static readonly string[] FatKeys = ["fatPer100g", "fat"];
    private static readonly string[] CarbsKeys = ["carbsPer100g", "carbohydratesPer100g", "carbs", "carbohydrates"];
    private static readonly string[] SugarKeys = ["sugarPer100g", "sugar"];
    private static readonly string[] KcalKeys = ["kcalPer100g", "kcal", "energyKcalPer100g"];

    public async Task<NutrientInfo?> ResolveAsync(string productId, string? barcode, CancellationToken cancellationToken = default)
    {
        var json = await mcpClient.CallToolAsync(
            "get_product_details",
            new Dictionary<string, object?> { ["productId"] = productId },
            cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var root = JsonDocument.Parse(json).RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new NutrientInfo(
            ReadNumber(root, ProteinKeys),
            ReadNumber(root, FatKeys),
            ReadNumber(root, CarbsKeys),
            ReadNumber(root, SugarKeys),
            ReadNumber(root, KcalKeys),
            Source: "mcp");
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
}
