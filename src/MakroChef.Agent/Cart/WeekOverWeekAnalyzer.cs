using System.Globalization;
using MakroChef.Agent.Coverage;
using MakroChef.Agent.Profiling;
using MakroChef.Domain.Nutrition;
using MakroChef.Mcp;
using MakroChef.Nutrition;

namespace MakroChef.Agent.Cart;

/// <summary>TASKS.md screen 6: is the guest's actual protein deficit shrinking? Groups real order
/// history by calendar week, resolves each purchased SKU's slug + nutrients once, and compares
/// the two most recent weeks' actual protein intake against the household's weekly target.
/// Retrospective only — there is no "current week so far" to compare against, just history.</summary>
public class WeekOverWeekAnalyzer(IMakroChefMcpClient mcpClient)
{
    public async Task<WeekOverWeekResult?> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        var session = await new SessionBootstrap(mcpClient).EnsureAsync(cancellationToken);
        if (session is null)
        {
            return null;
        }

        var profile = await new GuestContextCollector(mcpClient).CollectAsync(cancellationToken);
        var coverage = await new CoverageProbe(mcpClient, session).RunAsync(cancellationToken);
        var mode = coverage.CoveragePercent >= 60 ? NutritionResolverMode.Exact : NutritionResolverMode.CategoryIndex;
        var nutritionResolver = NutritionResolverFactory.Create(mode, mcpClient, session);

        var sessionArgs = new Dictionary<string, object?>
        {
            ["branchId"] = session.BranchId,
            ["deliveryType"] = session.DeliveryType,
            ["timeslotStart"] = session.TimeslotStart,
            ["timeslotEnd"] = session.TimeslotEnd,
        };
        var offlineJson = await mcpClient.CallToolAsync("silpo_get_my_offline_orders", sessionArgs, cancellationToken);
        var onlineJson = await mcpClient.CallToolAsync("silpo_get_my_online_orders", sessionArgs, cancellationToken);

        var items = new List<(DateTimeOffset Date, string ProductId, int Quantity)>();
        items.AddRange(JsonFieldScanner.ExtractOrderItems(offlineJson));
        items.AddRange(JsonFieldScanner.ExtractOrderItems(onlineJson));

        var byWeek = items
            .GroupBy(i => ISOWeek.GetWeekOfYear(i.Date.UtcDateTime) + i.Date.Year * 100)
            .OrderByDescending(g => g.Key)
            .ToList();

        if (byWeek.Count < 2)
        {
            return new WeekOverWeekResult(HasEnoughData: false, 0, 0);
        }

        var uniqueProductIds = items.Select(i => i.ProductId).Distinct().ToList();
        var batchJson = await mcpClient.CallToolAsync(
            "silpo_find_products_batch",
            new Dictionary<string, object?>
            {
                ["branchId"] = session.BranchId,
                ["deliveryType"] = session.DeliveryType,
                ["timeslotStart"] = session.TimeslotStart,
                ["timeslotEnd"] = session.TimeslotEnd,
                ["products"] = uniqueProductIds,
            },
            cancellationToken);
        var slugsById = JsonFieldScanner.ExtractProductSlugs(batchJson);

        var proteinPer100gById = new Dictionary<string, decimal>();
        foreach (var productId in uniqueProductIds)
        {
            if (!slugsById.TryGetValue(productId, out var slug))
            {
                continue;
            }

            NutrientInfo? nutrients;
            try
            {
                nutrients = await nutritionResolver.ResolveAsync(slug, barcode: null, cancellationToken);
            }
            catch (Exception)
            {
                // Confirmed live (2026-09-14): a delisted historical purchase can make
                // get_product_details return a plain-text error instead of JSON - skip it rather
                // than sinking the whole week-over-week comparison.
                continue;
            }

            if (nutrients?.ProteinPer100g is not null)
            {
                proteinPer100gById[productId] = nutrients.ProteinPer100g.Value;
            }
        }

        var norms = new TargetNormsCalculator().Compute(profile, medianDailyKcal: null);
        var weeklyProteinTargetGrams = norms.ProteinTargetGrams * 7;

        var thisWeekConsumed = ConsumedProteinGrams(byWeek[0], proteinPer100gById);
        var lastWeekConsumed = ConsumedProteinGrams(byWeek[1], proteinPer100gById);

        return new WeekOverWeekResult(
            HasEnoughData: true,
            LastWeekProteinGapGrams: Math.Max(0, Math.Round(weeklyProteinTargetGrams - lastWeekConsumed, 1)),
            ThisWeekProteinGapGrams: Math.Max(0, Math.Round(weeklyProteinTargetGrams - thisWeekConsumed, 1)));
    }

    /// <summary>Assumes each line item is a ~100g-equivalent unit since order history doesn't
    /// carry per-line weight (only get_product_details does, and resolving that per historical
    /// purchase would multiply MCP calls many times over) — a documented approximation, not
    /// invented precision.</summary>
    private static decimal ConsumedProteinGrams(
        IGrouping<int, (DateTimeOffset Date, string ProductId, int Quantity)> week,
        IReadOnlyDictionary<string, decimal> proteinPer100gById) =>
        week.Sum(i => proteinPer100gById.GetValueOrDefault(i.ProductId) * i.Quantity);
}
