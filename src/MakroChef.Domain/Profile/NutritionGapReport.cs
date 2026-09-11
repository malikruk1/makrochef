namespace MakroChef.Domain.Profile;

public record NutritionGapReport(decimal ConsumedProteinGrams, decimal TargetProteinGrams, string NormSource)
{
    public decimal ProteinDeficitGrams => Math.Max(0, TargetProteinGrams - ConsumedProteinGrams);

    public double CoveragePercent => TargetProteinGrams <= 0
        ? 100.0
        : Math.Min(100.0, 100.0 * (double)(ConsumedProteinGrams / TargetProteinGrams));
}
