namespace MakroChef.Domain.Cart;

public record CartValidationIssue(string ProductId, string Reason)
{
    /// <summary>TASKS.md 7.2: "не довіряти success: true — перечитувати кошик" and re-solve
    /// when a line is actually unavailable. The exact reason string is still unknown (needs
    /// BLOCKERS.md B-2), so this matches loosely rather than on one exact literal.</summary>
    public bool IsOutOfStock => Reason.Contains("stock", StringComparison.OrdinalIgnoreCase)
        || Reason.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
        || Reason.Contains("немає", StringComparison.OrdinalIgnoreCase);
}
