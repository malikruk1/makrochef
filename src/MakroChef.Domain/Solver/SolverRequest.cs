namespace MakroChef.Domain.Solver;

public record SolverRequest(
    IReadOnlyList<Candidate> Candidates,
    long TargetProteinMg,
    long MaxSugarMg,
    long KcalMin,
    long KcalMax,
    long BaselineCostKopecks,
    // Confirmed live (2026-09-14): with a real 173-candidate pool spread across 6 deficit
    // categories, a cap of 3 made the weekly protein target structurally unreachable by ~2.7%
    // (1,021,450mg max achievable vs a 1,050,000mg target) - purely from this cap, independent of
    // budget/sugar/kcal. 5 keeps the "don't buy 20 units of one product" diversity guardrail
    // while giving a weekly household basket enough room across a handful of categories.
    int MaxUnitsPerCategory = 5);
