using MakroChef.Agent.Coverage;
using MakroChef.Domain.Cart;
using MakroChef.Domain.Nutrition;
using MakroChef.Domain.Solver;
using MakroChef.Mcp;

namespace MakroChef.Agent.Catalog;

/// <summary>TASKS.md 6.1: assembles the 200-400 product pool the solver picks from.
///
/// Confirmed live (2026-09-14): silpo_find_products_batch/get_products/get_product_details all
/// need branchId + deliveryType + timeslotStart + timeslotEnd (from SessionBootstrap), and
/// get_product_details specifically needs the product's SLUG, not its id — the slug only ever
/// appears alongside the id in find_products_batch/get_products results
/// (JsonFieldScanner.ExtractProductSlugs), so it must be captured at pool-assembly time rather
/// than re-derived later.
///
/// The exact 200-400 count still needs a live account with a real catalog to fully validate at
/// that scale (BLOCKERS.md) - this builder is complete and correct, but a stub server can't fake
/// a whole supermarket's inventory, so local tests verify the pipeline (parsing, discounts,
/// restrictions, nutrient conversion, slug resolution) at a smaller scale.
///
/// Confirmed live (2026-09-14): silpo_get_products' "category" filter needs a real category slug
/// from silpo_get_categories, not a guessed free-text word - see CategoryResolver.
///
/// Confirmed live (2026-09-14): real get_product_details never returns a "category" field at all
/// (ProductDetailsParser falls back to "невідома" for every single product). BasketSolver caps
/// units PER category to enforce basket diversity - with every real candidate falling into one
/// "невідома" bucket, that cap collapsed into a global 3-unit limit on the entire 173-item pool,
/// which is why the solver stayed infeasible even with a correct budget (BLOCKERS.md #18). Fixed
/// by tagging each candidate found via the deficit-category search with the search keyword itself
/// (a real, meaningful grouping we already have for free) instead of trusting the absent field.
/// Seed products (from receipt history, not a category search) keep the parsed/fallback category
/// since there's no better signal for them.</summary>
public class CandidatePoolBuilder(IMakroChefMcpClient mcpClient, INutritionResolver nutritionResolver, SessionContext session)
{
    public async Task<IReadOnlyList<Candidate>> BuildAsync(CandidatePoolRequest request, CancellationToken cancellationToken = default)
    {
        // Confirmed live (2026-09-14): silpo_find_products_batch's "products" parameter is a TEXT
        // SEARCH (its own description: "semicolon-separated" search terms), not an id lookup -
        // resolving seed slugs through it always returned zero matches, so the guest's own
        // purchase history never actually contributed a single real seed candidate (BLOCKERS.md).
        // The caller now resolves these slugs directly from the order JSON itself and passes them
        // straight in - no MCP call needed for this step at all.
        var slugsById = new Dictionary<string, string>(request.SeedSlugsById);
        var categoryHintById = new Dictionary<string, string>();

        var categoryResolver = new CategoryResolver(mcpClient, session);
        foreach (var keyword in request.DeficitCategories)
        {
            var categorySlugs = await categoryResolver.ResolveSlugsAsync(keyword, cancellationToken);
            foreach (var categorySlug in categorySlugs)
            {
                var productsJson = await mcpClient.CallToolAsync(
                    "silpo_get_products",
                    new Dictionary<string, object?>
                    {
                        ["branchId"] = session.BranchId,
                        ["deliveryType"] = session.DeliveryType,
                        ["timeslotStart"] = session.TimeslotStart,
                        ["timeslotEnd"] = session.TimeslotEnd,
                        ["category"] = categorySlug,
                    },
                    cancellationToken);
                var foundInThisCategory = JsonFieldScanner.ExtractProductSlugs(productsJson);
                foreach (var (id, slug) in foundInThisCategory)
                {
                    slugsById[id] = slug;
                    categoryHintById.TryAdd(id, keyword);
                }
            }
        }

        // Confirmed live (2026-09-14): resolving ~178 real candidates one at a time (each its own
        // get_product_details + nutrition-resolver round trip to MCP) took 90-100s end to end -
        // almost entirely network wait, not CPU. Bounded concurrency brings that down without
        // hammering the real MCP server the way unlimited parallelism would.
        const int maxConcurrency = 12;
        using var throttle = new SemaphoreSlim(maxConcurrency);

        var tasks = slugsById.Select(async pair =>
        {
            var (productId, slug) = pair;
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var categoryHint = categoryHintById.GetValueOrDefault(productId);
                return await ResolveCandidateAsync(productId, slug, categoryHint, request.RestrictedCategories, cancellationToken);
            }
            finally
            {
                throttle.Release();
            }
        });

        var resolved = await Task.WhenAll(tasks);
        return resolved.Where(c => c is not null).Select(c => c!).ToList();
    }

    private async Task<Candidate?> ResolveCandidateAsync(
        string productId, string slug, string? categoryHint, IReadOnlyList<string> restrictedCategories, CancellationToken cancellationToken)
    {
        try
        {
            var detailsJson = await mcpClient.CallToolAsync(
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

            var details = ProductDetailsParser.Parse(productId, detailsJson);
            var category = categoryHint ?? details.Category;

            var nutrients = await nutritionResolver.ResolveAsync(slug, details.Barcode, cancellationToken);
            if (nutrients is null)
            {
                return null; // no usable nutrient data - can't let the solver reason about it
            }

            var weightFactor = details.WeightGrams / 100m;
            var restricted = restrictedCategories.Contains(category, StringComparer.OrdinalIgnoreCase);

            return new Candidate(
                ProductId: productId,
                Category: category,
                PriceKopecks: details.PriceKopecks,
                ProteinMg: ToMilligrams(nutrients.ProteinPer100g, weightFactor),
                SugarMg: ToMilligrams(nutrients.SugarPer100g, weightFactor),
                Kcal: (long)Math.Round((nutrients.KcalPer100g ?? 0) * weightFactor),
                Restricted: restricted,
                // Confirmed live (2026-09-14): the record's own default (4) let the solver pile up
                // on one cheap high-protein SKU (e.g. one peanut product x4) to fill most of the
                // whole 5-unit category cap alone - a real basket, but a visually repetitive one
                // for a demo. Capping any single real product at 2 units forces the category cap
                // to be filled by at least 3 distinct products instead.
                MaxUnits: 2,
                Name: details.Name,
                CompanyId: details.CompanyId);
        }
        catch (Exception)
        {
            // Confirmed live (2026-09-14): a delisted/unavailable product can make the real MCP
            // server return a plain-text error instead of JSON at any step here - one bad
            // candidate must not sink the whole pool, same as the "no usable nutrient data" skip.
            return null;
        }
    }

    private static long ToMilligrams(decimal? gramsPer100g, decimal weightFactor) =>
        (long)Math.Round((gramsPer100g ?? 0) * weightFactor * 1000);
}
