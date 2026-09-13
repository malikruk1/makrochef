using MakroChef.Agent.Catalog;
using MakroChef.Nutrition;

namespace MakroChef.Agent.Coverage;

/// <summary>Delegates to the same real-shape parsers used everywhere else
/// (ExactMcpNutritionResolver.ParseNutrients, ProductDetailsParser) instead of keeping a
/// second, separately-guessed set of field names — this used to assume English keys
/// ("proteinPer100g" etc.) at the JSON root; the real payload nests Ukrainian-labeled
/// nutrients under product.attributes (confirmed live, 2026-09-14).</summary>
public static class NutrientCompletenessChecker
{
    public static bool HasFullMacros(string productDetailsJson)
    {
        var (protein, fat, carbs, sugar, _) = ExactMcpNutritionResolver.ParseNutrients(productDetailsJson);
        return protein is not null && fat is not null && carbs is not null && sugar is not null;
    }

    public static string ExtractCategory(string productDetailsJson) =>
        ProductDetailsParser.Parse("_", productDetailsJson).Category;
}
