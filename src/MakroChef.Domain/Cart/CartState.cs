namespace MakroChef.Domain.Cart;

public record CartState(IReadOnlyList<CartLine> Lines, IReadOnlyList<CartValidationIssue> Validations, long TotalKopecks);
