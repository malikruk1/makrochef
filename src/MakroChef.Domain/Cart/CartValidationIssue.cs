namespace MakroChef.Domain.Cart;

public record CartValidationIssue(string ProductId, string Reason)
{
    /// <summary>TASKS.md 7.2: "не довіряти success: true — перечитувати кошик" and re-solve
    /// when a line is actually unavailable. Confirmed against a live cart (2026-09):
    /// cart.calculation.validations[].message is a dot-namespaced identifier, e.g.
    /// "product.offer.status.not_available" or "product.offer.stock.max" — not a sentence.</summary>
    public bool IsOutOfStock => Reason.Contains("stock", StringComparison.OrdinalIgnoreCase)
        || Reason.Contains("not_available", StringComparison.OrdinalIgnoreCase)
        || Reason.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
        || Reason.Contains("немає", StringComparison.OrdinalIgnoreCase);
}
