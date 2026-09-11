namespace MakroChef.Domain.Solver;

/// <summary>A candidate product for the solver. Nutrients and price are per single unit,
/// in integer sub-units (kopecks for price, milligrams for protein/sugar) because CP-SAT
/// only works with integers.</summary>
public record Candidate(
    string ProductId,
    string Category,
    long PriceKopecks,
    long ProteinMg,
    long SugarMg,
    long Kcal,
    bool Restricted,
    int MaxUnits = 4);
