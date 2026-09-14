using MakroChef.Agent.Coverage;
using MakroChef.Domain.Cart;
using MakroChef.Domain.Catalog;
using MakroChef.Domain.Nutrition;
using MakroChef.Mcp;

namespace MakroChef.Agent.Catalog;

/// <summary>TASKS.md 6.2: for each product in the guest's usual basket, find a same-category
/// alternative via get_similar_products and compute the real delta. Only surfaces swaps that
/// are an actual improvement — more protein or less sugar, never "technically different but
/// worse both ways".
///
/// Confirmed live (2026-09-14): get_product_details needs a slug + branchId/deliveryType/
/// timeslot, not a bare productId - the guest's "usual" product needs its slug resolved via
/// find_products_batch first (its id alone, e.g. from receipt history, isn't enough).
///
/// Confirmed live (2026-09-14) via the tool's real input schema: silpo_get_similar_products
/// requires "slug" (not "productId") alongside branchId/deliveryType/timeslot - the previous call
/// always failed MCP input validation, so no swap was ever actually surfaced.</summary>
public class SwapGenerator(IMakroChefMcpClient mcpClient, INutritionResolver nutritionResolver, SessionContext session)
{
    public async Task<IReadOnlyList<ProductSwap>> GenerateAsync(IReadOnlyList<string> usualCartProductIds, CancellationToken cancellationToken = default)
    {
        var swaps = new List<ProductSwap>();

        foreach (var productId in usualCartProductIds)
        {
            var oldSlug = await ResolveSlugAsync(productId, cancellationToken);
            if (oldSlug is null)
            {
                continue;
            }

            ProductDetails oldDetails;
            NutrientInfo oldNutrients;
            try
            {
                var oldDetailsJson = await GetProductDetailsAsync(oldSlug, cancellationToken);
                oldDetails = ProductDetailsParser.Parse(productId, oldDetailsJson);
                var resolved = await nutritionResolver.ResolveAsync(oldSlug, oldDetails.Barcode, cancellationToken);
                if (resolved is null)
                {
                    continue;
                }

                oldNutrients = resolved;
            }
            catch (Exception)
            {
                // Confirmed live (2026-09-14): a delisted "usual" product can make
                // get_product_details return a plain-text error instead of JSON - can't build any
                // swaps off it, but the rest of the guest's basket still can be.
                continue;
            }

            string similarJson;
            try
            {
                similarJson = await mcpClient.CallToolAsync(
                    "silpo_get_similar_products",
                    new Dictionary<string, object?>
                    {
                        ["slug"] = oldSlug,
                        ["branchId"] = session.BranchId,
                        ["deliveryType"] = session.DeliveryType,
                        ["timeslotStart"] = session.TimeslotStart,
                        ["timeslotEnd"] = session.TimeslotEnd,
                    },
                    cancellationToken);
            }
            catch (Exception)
            {
                // Same tolerance as elsewhere - one product's similar-products lookup failing
                // must not sink swap suggestions for the rest of the basket.
                continue;
            }

            foreach (var (candidateId, candidateSlug) in JsonFieldScanner.ExtractProductSlugs(similarJson))
            {
                if (candidateId == productId)
                {
                    continue;
                }

                var swap = await TryBuildSwapAsync(productId, oldDetails, oldNutrients, candidateId, candidateSlug, cancellationToken);
                if (swap is not null)
                {
                    swaps.Add(swap);
                }
            }
        }

        return swaps;
    }

    private async Task<string?> ResolveSlugAsync(string productId, CancellationToken cancellationToken)
    {
        var batchJson = await mcpClient.CallToolAsync(
            "silpo_find_products_batch",
            new Dictionary<string, object?>
            {
                ["branchId"] = session.BranchId,
                ["deliveryType"] = session.DeliveryType,
                ["timeslotStart"] = session.TimeslotStart,
                ["timeslotEnd"] = session.TimeslotEnd,
                ["products"] = new[] { productId },
            },
            cancellationToken);

        return JsonFieldScanner.ExtractProductSlugs(batchJson).GetValueOrDefault(productId);
    }

    private Task<string> GetProductDetailsAsync(string slug, CancellationToken cancellationToken) =>
        mcpClient.CallToolAsync(
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

    private async Task<ProductSwap?> TryBuildSwapAsync(
        string oldProductId, ProductDetails oldDetails, NutrientInfo oldNutrients, string candidateId, string candidateSlug, CancellationToken cancellationToken)
    {
        try
        {
            var newDetailsJson = await GetProductDetailsAsync(candidateSlug, cancellationToken);
            var newDetails = ProductDetailsParser.Parse(candidateId, newDetailsJson);

            var newNutrients = await nutritionResolver.ResolveAsync(candidateSlug, newDetails.Barcode, cancellationToken);
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

            return new ProductSwap(oldProductId, candidateId, proteinDelta, sugarDelta, priceDelta, newDetails.OnPromotion, oldDetails.Name, newDetails.Name);
        }
        catch (Exception)
        {
            // Confirmed live (2026-09-14): a delisted candidate can make get_product_details (or
            // the nutrition resolver's own call) return a plain-text error instead of JSON - just
            // not a swap worth offering.
            return null;
        }
    }
}
