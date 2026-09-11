namespace MakroChef.Domain.Cart;

public record ReoptimizationResult(bool FullyResolved, int Iterations, CartState FinalCart, IReadOnlyList<string> DegradedNotes);
