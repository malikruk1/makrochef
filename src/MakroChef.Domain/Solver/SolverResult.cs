namespace MakroChef.Domain.Solver;

public record BasketLine(string ProductId, int Units);

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
