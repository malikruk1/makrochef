namespace MakroChef.Domain.Nutrition;

public record NutrientInfo(
    decimal? ProteinPer100g,
    decimal? FatPer100g,
    decimal? CarbsPer100g,
    decimal? SugarPer100g,
    decimal? KcalPer100g,
    string Source);
