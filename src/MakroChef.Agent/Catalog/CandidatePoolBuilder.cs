using MakroChef.Agent.Coverage;
using MakroChef.Domain.Nutrition;
using MakroChef.Domain.Solver;
using MakroChef.Mcp;

namespace MakroChef.Agent.Catalog;

/// <summary>TASKS.md 6.1: assembles the 200-400 product pool the solver picks from. The exact
/// 200-400 count needs a live account with a real catalog (BLOCKERS.md B-2) - this builder is
/// complete and correct, but a stub server can't fake a whole supermarket's inventory, so local
/// tests verify the pipeline (parsing, discounts, restrictions, nutrient conversion) at a
/// smaller scale.</summary>
public class CandidatePoolBuilder(IMakroChefMcpClient mcpClient, INutritionResolver nutritionResolver)
{
    public async Task<IReadOnlyList<Candidate>> BuildAsync(CandidatePoolRequest request, CancellationToken cancellationToken = default)
    {
        var productIds = new HashSet<string>();

        if (request.SeedProductIds.Count > 0)
        {
            var batchJson = await mcpClient.CallToolAsync(
                "find_products_batch",
                new Dictionary<string, object?> { ["productIds"] = request.SeedProductIds },
                cancellationToken);
            productIds.UnionWith(JsonFieldScanner.ExtractProductIds(batchJson));
        }

        foreach (var category in request.DeficitCategories)
        {
            var productsJson = await mcpClient.CallToolAsync(
                "get_products",
                new Dictionary<string, object?> { ["category"] = category, ["onPromotion"] = true },
                cancellationToken);
            productIds.UnionWith(JsonFieldScanner.ExtractProductIds(productsJson));
        }

        var candidates = new List<Candidate>();
        foreach (var productId in productIds)
        {
            var candidate = await ResolveCandidateAsync(productId, request.RestrictedCategories, cancellationToken);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private async Task<Candidate?> ResolveCandidateAsync(string productId, IReadOnlyList<string> restrictedCategories, CancellationToken cancellationToken)
    {
        var detailsJson = await mcpClient.CallToolAsync(
            "get_product_details",
            new Dictionary<string, object?> { ["productId"] = productId },
            cancellationToken);
        var details = ProductDetailsParser.Parse(productId, detailsJson);

        var nutrients = await nutritionResolver.ResolveAsync(productId, details.Barcode, cancellationToken);
        if (nutrients is null)
        {
            return null; // no usable nutrient data - can't let the solver reason about it
        }

        var weightFactor = details.WeightGrams / 100m;
        var restricted = restrictedCategories.Contains(details.Category, StringComparer.OrdinalIgnoreCase);

        return new Candidate(
            ProductId: productId,
            Category: details.Category,
            PriceKopecks: details.PriceKopecks,
            ProteinMg: ToMilligrams(nutrients.ProteinPer100g, weightFactor),
            SugarMg: ToMilligrams(nutrients.SugarPer100g, weightFactor),
            Kcal: (long)Math.Round((nutrients.KcalPer100g ?? 0) * weightFactor),
            Restricted: restricted);
    }

    private static long ToMilligrams(decimal? gramsPer100g, decimal weightFactor) =>
        (long)Math.Round((gramsPer100g ?? 0) * weightFactor * 1000);
}
