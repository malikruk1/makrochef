using MakroChef.Domain.Nutrition;

namespace MakroChef.Nutrition;

/// <summary>Path B (TASKS.md 4.0): coverage &lt; 60% -> MCP nutrient data is too sparse to
/// trust product-by-product, so try the exact resolver first (some products DO have full
/// data even in a low-coverage account) and fall back to Open Food Facts by barcode.</summary>
public class CategoryIndexNutritionResolver(ExactMcpNutritionResolver exactResolver, OpenFoodFactsClient openFoodFacts) : INutritionResolver
{
    public async Task<NutrientInfo?> ResolveAsync(string productId, string? barcode, CancellationToken cancellationToken = default)
    {
        var exact = await exactResolver.ResolveAsync(productId, barcode, cancellationToken);
        if (exact is { ProteinPer100g: not null, SugarPer100g: not null })
        {
            return exact;
        }

        return string.IsNullOrWhiteSpace(barcode)
            ? exact
            : await openFoodFacts.FetchAsync(barcode, cancellationToken) ?? exact;
    }
}
