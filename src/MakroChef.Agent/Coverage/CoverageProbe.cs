using MakroChef.Domain.Cart;
using MakroChef.Mcp;

namespace MakroChef.Agent.Coverage;

/// <summary>TASKS.md 3.4: get_my_offline_orders + get_my_online_orders -> for each unique SKU,
/// get_product_details (through the caller's caching/backoff-wrapped client) -> report.
/// Known systematic gaps (weighed goods, own-production, fresh) are flagged when their
/// category is either absent from the basket history or mostly missing full macros.
///
/// Confirmed live (2026-09-14): silpo_get_my_offline_orders also requires branchId/deliveryType/
/// timeslotStart/timeslotEnd (SessionBootstrap), same as every catalog tool, and
/// get_product_details needs a slug — order history only carries bare ids, so each id's slug is
/// resolved via silpo_find_products_batch first (JsonFieldScanner.ExtractProductSlugs).</summary>
public class CoverageProbe(IMakroChefMcpClient client, SessionContext session)
{
    private static readonly string[] KnownGapCategories = ["ваговий", "власне виробництво", "фреш"];

    public async Task<CoverageReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var sessionArgs = new Dictionary<string, object?>
        {
            ["branchId"] = session.BranchId,
            ["deliveryType"] = session.DeliveryType,
            ["timeslotStart"] = session.TimeslotStart,
            ["timeslotEnd"] = session.TimeslotEnd,
        };

        var offlineOrdersJson = await client.CallToolAsync("silpo_get_my_offline_orders", sessionArgs, cancellationToken);
        var onlineOrdersJson = await client.CallToolAsync("silpo_get_my_online_orders", sessionArgs, cancellationToken);

        var productIds = new HashSet<string>();
        productIds.UnionWith(JsonFieldScanner.ExtractProductIds(offlineOrdersJson));
        productIds.UnionWith(JsonFieldScanner.ExtractProductIds(onlineOrdersJson));

        var orderTotals = new List<(decimal Amount, DateTimeOffset Date)>();
        orderTotals.AddRange(JsonFieldScanner.ExtractOrderTotals(offlineOrdersJson));
        orderTotals.AddRange(JsonFieldScanner.ExtractOrderTotals(onlineOrdersJson));

        var fullMacroCount = 0;
        var totalByCategory = new Dictionary<string, int>();
        var fullByCategory = new Dictionary<string, int>();

        foreach (var productId in productIds)
        {
            try
            {
                var slug = await ResolveSlugAsync(productId, cancellationToken) ?? productId;
                var detailsJson = await client.CallToolAsync(
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

                var category = NutrientCompletenessChecker.ExtractCategory(detailsJson);
                totalByCategory[category] = totalByCategory.GetValueOrDefault(category) + 1;

                if (NutrientCompletenessChecker.HasFullMacros(detailsJson))
                {
                    fullMacroCount++;
                    fullByCategory[category] = fullByCategory.GetValueOrDefault(category) + 1;
                }
            }
            catch (Exception)
            {
                // A single historical SKU that's since been discontinued/delisted can make the
                // real MCP server return a plain-text error instead of JSON (confirmed live,
                // 2026-09-14) - one bad product must not sink the whole coverage report, just
                // like an unresolvable nutrient already skips that one candidate elsewhere.
            }
        }

        var gaps = FindGaps(totalByCategory, fullByCategory);
        var medianWeeklyReceipt = ComputeMedianWeeklyReceipt(orderTotals);

        return new CoverageReport(productIds.Count, fullMacroCount, totalByCategory, gaps, medianWeeklyReceipt);
    }

    private async Task<string?> ResolveSlugAsync(string productId, CancellationToken cancellationToken)
    {
        var batchJson = await client.CallToolAsync(
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

    private static List<string> FindGaps(Dictionary<string, int> totalByCategory, Dictionary<string, int> fullByCategory)
    {
        var gaps = new List<string>();
        foreach (var gapCategory in KnownGapCategories)
        {
            var matchingKey = totalByCategory.Keys.FirstOrDefault(k => k.Contains(gapCategory, StringComparison.OrdinalIgnoreCase));
            if (matchingKey is null)
            {
                gaps.Add($"{gapCategory} (0%)");
                continue;
            }

            var total = totalByCategory[matchingKey];
            var full = fullByCategory.GetValueOrDefault(matchingKey);
            var ratio = total == 0 ? 0 : 100.0 * full / total;
            if (ratio < 50)
            {
                gaps.Add($"{gapCategory} ({ratio:F0}%)");
            }
        }

        return gaps;
    }

    private static decimal? ComputeMedianWeeklyReceipt(IReadOnlyList<(decimal Amount, DateTimeOffset Date)> orders)
    {
        if (orders.Count == 0)
        {
            return null;
        }

        var weeklyTotals = orders
            .GroupBy(o => System.Globalization.ISOWeek.GetWeekOfYear(o.Date.UtcDateTime) + o.Date.Year * 100)
            .Select(g => g.Sum(o => o.Amount))
            .OrderBy(sum => sum)
            .ToList();

        var mid = weeklyTotals.Count / 2;
        return weeklyTotals.Count % 2 == 0
            ? (weeklyTotals[mid - 1] + weeklyTotals[mid]) / 2
            : weeklyTotals[mid];
    }
}
