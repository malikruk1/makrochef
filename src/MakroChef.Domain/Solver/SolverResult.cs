namespace MakroChef.Domain.Solver;

// Confirmed live (2026-09-14): silpo_add_or_update_cart_products' own tool description says it
// "Requires productId, companyId, and branchId" - CompanyId must travel with each line all the
// way from the resolved Candidate, not be assumed away.
public record BasketLine(string ProductId, int Units, string? CompanyId = null);

public record SolverResult(
    bool Success,
    IReadOnlyList<BasketLine> Lines,
    long TotalCostKopecks,
    long TotalProteinMg,
    long TotalSugarMg,
    long TotalKcal,
    IReadOnlyList<string> Relaxed)
{
    public static SolverResult Failure(IReadOnlyList<string> relaxed) =>
        new(false, [], 0, 0, 0, 0, relaxed);
}
