namespace MakroChef.Domain.Catalog;

/// <summary>TASKS.md 6.2: a 1-for-1 swap within the same category, never "buy quinoa instead" -
/// this is a real retail switch a guest recognizes.</summary>
public record ProductSwap(
    string OldProductId,
    string NewProductId,
    decimal ProteinDeltaGrams,
    decimal SugarDeltaGrams,
    long PriceDeltaKopecks,
    bool OnPromotion,
    string? OldName = null,
    string? NewName = null);
