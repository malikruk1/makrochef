using MakroChef.Mcp;

namespace MakroChef.Agent.Coverage;

/// <summary>TASKS.md 3.4: get_my_offline_orders + get_my_online_orders -> for each unique SKU,
/// get_product_details (through the caller's caching/backoff-wrapped client) -> report.
/// Known systematic gaps (weighed goods, own-production, fresh) are flagged when their
/// category is either absent from the basket history or mostly missing full macros.</summary>
public class CoverageProbe(IMakroChefMcpClient client)
{
    private static readonly string[] KnownGapCategories = ["ваговий", "власне виробництво", "фреш"];

    public async Task<CoverageReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var offlineOrdersJson = await client.CallToolAsync("silpo_get_my_offline_orders", new Dictionary<string, object?>(), cancellationToken);
        var onlineOrdersJson = await client.CallToolAsync("silpo_get_my_online_orders", new Dictionary<string, object?>(), cancellationToken);

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
            var detailsJson = await client.CallToolAsync(
                "silpo_get_product_details",
                new Dictionary<string, object?> { ["productId"] = productId },
                cancellationToken);

            var category = NutrientCompletenessChecker.ExtractCategory(detailsJson);
            totalByCategory[category] = totalByCategory.GetValueOrDefault(category) + 1;

            if (NutrientCompletenessChecker.HasFullMacros(detailsJson))
            {
                fullMacroCount++;
                fullByCategory[category] = fullByCategory.GetValueOrDefault(category) + 1;
            }
        }

        var gaps = FindGaps(totalByCategory, fullByCategory);
        var medianWeeklyReceipt = ComputeMedianWeeklyReceipt(orderTotals);

        return new CoverageReport(productIds.Count, fullMacroCount, totalByCategory, gaps, medianWeeklyReceipt);
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
