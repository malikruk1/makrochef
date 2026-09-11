namespace MakroChef.Domain.Entities;

public class ProductNutrition
{
    public Guid Id { get; set; }
    public required string ProductId { get; set; }
    public required string Name { get; set; }
    public string? Category { get; set; }
    public decimal? ProteinPer100g { get; set; }
    public decimal? SugarPer100g { get; set; }
    public decimal? FatPer100g { get; set; }
    public decimal? KcalPer100g { get; set; }
    public string Source { get; set; } = "mcp";
    public DateTimeOffset FetchedAt { get; set; }
}
