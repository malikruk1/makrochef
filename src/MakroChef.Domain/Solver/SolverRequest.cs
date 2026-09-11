namespace MakroChef.Domain.Solver;

public record SolverRequest(
    IReadOnlyList<Candidate> Candidates,
    long TargetProteinMg,
    long MaxSugarMg,
    long KcalMin,
    long KcalMax,
    long BaselineCostKopecks,
    int MaxUnitsPerCategory = 3);
