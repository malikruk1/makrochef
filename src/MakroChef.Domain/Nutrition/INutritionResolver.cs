namespace MakroChef.Domain.Nutrition;

/// <summary>TASKS.md 4.0: which of the two implementations backs this depends on the real
/// coverage % from the 3.4 probe (>=60% -> exact; <60% -> category index + Open Food Facts
/// fallback) — a decision that needs live data we don't have yet (BLOCKERS.md B-5). Both
/// implementations exist and are switched by config so neither choice blocks the rest of the
/// app from being built.</summary>
public interface INutritionResolver
{
    Task<NutrientInfo?> ResolveAsync(string productId, string? barcode, CancellationToken cancellationToken = default);
}
