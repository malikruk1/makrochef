namespace MakroChef.Domain.Profile;

/// <summary>TASKS.md 4.3: never compare a delivered basket against an in-store receipt that
/// carries no delivery fee — keep items and delivery visible separately so that comparison
/// stays honest.</summary>
public record BaselineCost(decimal ItemsKopecks, decimal DeliveryKopecks)
{
    public decimal TotalKopecks => ItemsKopecks + DeliveryKopecks;
}
