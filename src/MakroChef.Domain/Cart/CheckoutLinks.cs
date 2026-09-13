namespace MakroChef.Domain.Cart;

public record CheckoutLinks(string? WebLink, string? MobileLink, long TotalKopecks = 0);
