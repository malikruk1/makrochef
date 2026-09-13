using MakroChef.Agent.Catalog;
using MakroChef.Agent.Coverage;
using MakroChef.Agent.Profiling;
using MakroChef.Agent.Tracing;
using MakroChef.Domain.Solver;
using MakroChef.Mcp;
using MakroChef.Nutrition;

namespace MakroChef.Agent.Cart;

/// <summary>TASKS.md 6: the agent's core loop end-to-end — profile → household weekly targets →
/// candidate pool → solver → comparison against the guest's real historical weekly spend. This
/// is the "screen 3" data source. Read-only: never writes to the guest's actual cart (TASKS.md
/// 7.1 requires an explicit, separately-confirmed action for that, not a side effect of
/// planning).
///
/// Deficit categories are a fixed hard-coded list (сир/риба/яйця/бобові/горіхи/молочні) since
/// silpo_get_categories_tree isn't wired yet (BLOCKERS.md) — a real deficit-driven category
/// choice (e.g. "guest is low on protein and has no fish in history" → prioritize риба) is a
/// documented gap, not invented here.</summary>
public class BasketPlanner(IMakroChefMcpClient mcpClient, LoggingBasketSolver solver)
{
    private static readonly string[] DeficitCategoryCandidates = ["сир", "риба", "яйця", "бобові", "горіхи", "молочні"];

    public async Task<BasketPlanResult?> PlanAsync(CancellationToken cancellationToken = default)
    {
        var session = await new SessionBootstrap(mcpClient).EnsureAsync(cancellationToken);
        if (session is null)
        {
            return null; // guest has no cart yet - caller must tell them to pick an address first
        }

        var profile = await new GuestContextCollector(mcpClient).CollectAsync(cancellationToken);
        var coverage = await new CoverageProbe(mcpClient, session).RunAsync(cancellationToken);

        var mode = coverage.CoveragePercent >= 60 ? NutritionResolverMode.Exact : NutritionResolverMode.CategoryIndex;
        var nutritionResolver = NutritionResolverFactory.Create(mode, mcpClient, session);

        var seedProductIds = await CollectSeedProductIdsAsync(session, cancellationToken);
        var deficitCategories = DeficitCategoryCandidates
            .Where(c => !profile.Restrictions.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var pool = await new CandidatePoolBuilder(mcpClient, nutritionResolver, session).BuildAsync(
            new CandidatePoolRequest(seedProductIds, deficitCategories, profile.Restrictions),
            cancellationToken);

        var norms = new TargetNormsCalculator().Compute(profile, medianDailyKcal: null);
        var baselineWeeklyCostKopecks = (long)Math.Round((coverage.MedianWeeklyReceipt ?? 0) * 100);

        var request = new SolverRequest(
            Candidates: pool,
            TargetProteinMg: (long)Math.Round(norms.ProteinTargetGrams * 7 * 1000),
            MaxSugarMg: (long)Math.Round(norms.MaxSugarGrams * 7 * 1000),
            KcalMin: (long)Math.Round(norms.KcalMin * 7),
            KcalMax: (long)Math.Round(norms.KcalMax * 7),
            BaselineCostKopecks: baselineWeeklyCostKopecks);

        var result = await solver.SolveAsync(request, cancellationToken);

        return new BasketPlanResult(session, norms, coverage, pool.Count, baselineWeeklyCostKopecks, result);
    }

    private async Task<List<string>> CollectSeedProductIdsAsync(Domain.Cart.SessionContext session, CancellationToken cancellationToken)
    {
        var sessionArgs = new Dictionary<string, object?>
        {
            ["branchId"] = session.BranchId,
            ["deliveryType"] = session.DeliveryType,
            ["timeslotStart"] = session.TimeslotStart,
            ["timeslotEnd"] = session.TimeslotEnd,
        };

        var offlineJson = await mcpClient.CallToolAsync("silpo_get_my_offline_orders", sessionArgs, cancellationToken);
        var onlineJson = await mcpClient.CallToolAsync("silpo_get_my_online_orders", sessionArgs, cancellationToken);

        var ids = new HashSet<string>();
        ids.UnionWith(JsonFieldScanner.ExtractProductIds(offlineJson));
        ids.UnionWith(JsonFieldScanner.ExtractProductIds(onlineJson));
        return ids.ToList();
    }
}
