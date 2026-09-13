namespace MakroChef.Domain.Cart;

/// <summary>WebLink/MobileLink are kept for API-shape stability but are always null in practice —
/// confirmed live (2026-09-14) that no real cart response carries a checkout link and no
/// silpo_checkout/place_order/pay tool exists at all (BLOCKERS.md B-6). BlockingValidations
/// surfaces the cart's real validation messages instead, since a non-empty list here is exactly
/// why the guest can't finish payment in the Silpo app right now.</summary>
public record CheckoutLinks(string? WebLink, string? MobileLink, long TotalKopecks = 0, IReadOnlyList<string>? BlockingValidations = null);
