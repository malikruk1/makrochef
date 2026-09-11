using System.Text.Json;

namespace MakroChef.Agent.Coverage;

public static class NutrientCompletenessChecker
{
    private static readonly string[] ProteinKeys = ["proteinPer100g", "protein"];
    private static readonly string[] FatKeys = ["fatPer100g", "fat"];
    private static readonly string[] CarbsKeys = ["carbsPer100g", "carbohydratesPer100g", "carbs", "carbohydrates"];
    private static readonly string[] SugarKeys = ["sugarPer100g", "sugar"];

    public static bool HasFullMacros(string productDetailsJson)
    {
        var root = JsonDocument.Parse(productDetailsJson).RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return HasAnyNumeric(root, ProteinKeys)
            && HasAnyNumeric(root, FatKeys)
            && HasAnyNumeric(root, CarbsKeys)
            && HasAnyNumeric(root, SugarKeys);
    }

    public static string ExtractCategory(string productDetailsJson)
    {
        var root = JsonDocument.Parse(productDetailsJson).RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("category", out var category)
            && category.ValueKind == JsonValueKind.String)
        {
            return category.GetString()!;
        }

        return "невідома";
    }

    private static bool HasAnyNumeric(JsonElement root, string[] keyCandidates)
    {
        foreach (var prop in root.EnumerateObject())
        {
            if (keyCandidates.Contains(prop.Name, StringComparer.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Number)
            {
                return true;
            }
        }

        return false;
    }
}
