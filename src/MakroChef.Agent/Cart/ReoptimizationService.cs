using MakroChef.Agent.Catalog;
using MakroChef.Agent.Coverage;
using MakroChef.Agent.Tracing;
using MakroChef.Domain.Cart;
using MakroChef.Domain.Nutrition;
using MakroChef.Domain.Solver;
using MakroChef.Mcp;

namespace MakroChef.Agent.Cart;

/// <summary>TASKS.md 7.2 — THE key point. Never a 1-for-1 substitution: an unavailable item is
/// excluded from the pool entirely, its get_replacements alternatives are added alongside it,
/// and the WHOLE basket is re-solved under the same constraints. Substituting a 40g-protein
/// yogurt for a 22g one and calling it done leaves the deficit worse and the rest of the basket
/// stale; re-solving might find it's now worth buying different cheese and one less pack of
/// nuts instead.
///
/// Confirmed live (2026-09-14): get_product_details needs a slug + branchId/deliveryType/
/// timeslot (SessionContext), not a bare productId - get_replacements results carry both id and
/// slug together, same as every other catalog tool.</summary>
public class ReoptimizationService(
    IMakroChefMcpClient mcpClient,
    INutritionResolver nutritionResolver,
    LoggingBasketSolver solver,
    BasketAssembler basketAssembler,
    SessionContext session)
{
    private const int MaxIterations = 3;

    public async Task<ReoptimizationResult> ReoptimizeAsync(
        SolverRequest baseRequest, CartState initialCart, CancellationToken cancellationToken = default)
    {
        var candidates = baseRequest.Candidates.ToList();
        var cart = initialCart;
        var degradedNotes = new List<string>();

        for (var iteration = 1; iteration <= MaxIterations; iteration++)
        {
            var problemProductIds = cart.Validations.Where(v => v.IsOutOfStock).Select(v => v.ProductId).Distinct().ToList();
            if (problemProductIds.Count == 0)
            {
                return new ReoptimizationResult(true, iteration - 1, cart, degradedNotes);
            }

            foreach (var problemProductId in problemProductIds)
            {
                candidates.RemoveAll(c => c.ProductId == problemProductId);

                var replacementsJson = await mcpClient.CallToolAsync(
                    "silpo_get_replacements",
                    new Dictionary<string, object?>
                    {
                        ["productId"] = problemProductId,
                        ["branchId"] = session.BranchId,
                        ["deliveryType"] = session.DeliveryType,
                        ["timeslotStart"] = session.TimeslotStart,
                        ["timeslotEnd"] = session.TimeslotEnd,
                    },
                    cancellationToken);

                foreach (var (replacementId, replacementSlug) in JsonFieldScanner.ExtractProductSlugs(replacementsJson))
                {
                    if (candidates.Any(c => c.ProductId == replacementId))
                    {
                        continue;
                    }

                    var candidate = await ResolveReplacementCandidateAsync(replacementId, replacementSlug, cancellationToken);
                    if (candidate is not null)
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            var solverResult = await solver.SolveAsync(baseRequest with { Candidates = candidates }, cancellationToken);
            if (!solverResult.Success)
            {
                degradedNotes.Add($"Ітерація {iteration}: солвер не знайшов рішення після виключення недоступних товарів ({string.Join(", ", problemProductIds)}).");
                return new ReoptimizationResult(false, iteration, cart, degradedNotes);
            }

            var newProductIds = solverResult.Lines.Select(l => l.ProductId).ToHashSet();
            var toRemove = cart.Lines.Select(l => l.ProductId).Where(id => !newProductIds.Contains(id)).ToList();

            await basketAssembler.RemoveAsync(toRemove, cancellationToken);
            await basketAssembler.AddOrUpdateAsync(solverResult.Lines, cancellationToken);

            cart = await basketAssembler.GetCartAsync(cancellationToken);
        }

        degradedNotes.Add($"Досягнуто ліміту {MaxIterations} ітерацій — кошик міг лишитись не повністю валідованим.");
        return new ReoptimizationResult(false, MaxIterations, cart, degradedNotes);
    }

    private async Task<Candidate?> ResolveReplacementCandidateAsync(string productId, string slug, CancellationToken cancellationToken)
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

        var nutrients = await nutritionResolver.ResolveAsync(slug, details.Barcode, cancellationToken);
        if (nutrients is null)
        {
            return null;
        }

        var weightFactor = details.WeightGrams / 100m;
        return new Candidate(
            ProductId: productId,
            Category: details.Category,
            PriceKopecks: details.PriceKopecks,
            ProteinMg: (long)Math.Round((nutrients.ProteinPer100g ?? 0) * weightFactor * 1000),
            SugarMg: (long)Math.Round((nutrients.SugarPer100g ?? 0) * weightFactor * 1000),
            Kcal: (long)Math.Round((nutrients.KcalPer100g ?? 0) * weightFactor),
            Restricted: false);
    }
}
