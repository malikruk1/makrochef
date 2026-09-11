using MakroChef.Agent.Coverage;
using MakroChef.Domain.Catalog;
using MakroChef.Domain.Nutrition;
using MakroChef.Mcp;

namespace MakroChef.Agent.Catalog;

/// <summary>TASKS.md 6.2: for each product in the guest's usual basket, find a same-category
/// alternative via get_similar_products and compute the real delta. Only surfaces swaps that
/// are an actual improvement — more protein or less sugar, never "technically different but
/// worse both ways".</summary>
public class SwapGenerator(IMakroChefMcpClient mcpClient, INutritionResolver nutritionResolver)
{
    public async Task<IReadOnlyList<ProductSwap>> GenerateAsync(IReadOnlyList<string> usualCartProductIds, CancellationToken cancellationToken = default)
    {
        var swaps = new List<ProductSwap>();

        foreach (var productId in usualCartProductIds)
        {
            var oldDetailsJson = await mcpClient.CallToolAsync(
                "get_product_details", new Dictionary<string, object?> { ["productId"] = productId }, cancellationToken);
            var oldDetails = ProductDetailsParser.Parse(productId, oldDetailsJson);
            var oldNutrients = await nutritionResolver.ResolveAsync(productId, oldDetails.Barcode, cancellationToken);
            if (oldNutrients is null)
            {
                continue;
            }

            var similarJson = await mcpClient.CallToolAsync(
                "get_similar_products", new Dictionary<string, object?> { ["productId"] = productId }, cancellationToken);

            foreach (var candidateId in JsonFieldScanner.ExtractProductIds(similarJson))
            {
                if (candidateId == productId)
                {
                    continue;
                }

                var swap = await TryBuildSwapAsync(productId, oldDetails, oldNutrients, candidateId, cancellationToken);
                if (swap is not null)
                {
                    swaps.Add(swap);
                }
            }
        }

        return swaps;
    }

    private async Task<ProductSwap?> TryBuildSwapAsync(
        string oldProductId, ProductDetails oldDetails, NutrientInfo oldNutrients, string candidateId, CancellationToken cancellationToken)
    {
        var newDetailsJson = await mcpClient.CallToolAsync(
            "get_product_details", new Dictionary<string, object?> { ["productId"] = candidateId }, cancellationToken);
        var newDetails = ProductDetailsParser.Parse(candidateId, newDetailsJson);
        var newNutrients = await nutritionResolver.ResolveAsync(candidateId, newDetails.Barcode, cancellationToken);
        if (newNutrients is null)
        {
            return null;
        }

        var proteinDelta = (newNutrients.ProteinPer100g ?? 0) - (oldNutrients.ProteinPer100g ?? 0);
        var sugarDelta = (newNutrients.SugarPer100g ?? 0) - (oldNutrients.SugarPer100g ?? 0);
        var priceDelta = newDetails.PriceKopecks - oldDetails.PriceKopecks;

        var isImprovement = proteinDelta > 0 || sugarDelta < 0;
        if (!isImprovement)
        {
            return null;
        }

        return new ProductSwap(oldProductId, candidateId, proteinDelta, sugarDelta, priceDelta, newDetails.OnPromotion);
    }
}
