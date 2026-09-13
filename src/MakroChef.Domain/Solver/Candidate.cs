namespace MakroChef.Domain.Solver;

/// <summary>A candidate product for the solver. Nutrients and price are per single unit,
/// in integer sub-units (kopecks for price, milligrams for protein/sugar) because CP-SAT
/// only works with integers.</summary>
public record Candidate(
    string ProductId,
    string Category,
    long PriceKopecks,
    long ProteinMg,
    long SugarMg,
    long Kcal,
    bool Restricted,
    int MaxUnits = 4,
    // Confirmed live (2026-09-14): real get_product_details carries a human-readable "name" -
    // null only for stub fixtures/tests that don't bother setting one.
    string? Name = null,
    // Confirmed live (2026-09-14): silpo_add_or_update_cart_products' own tool description says
    // it requires companyId alongside productId/branchId - must be carried per-candidate, not
    // assumed to be a single server-side constant.
    string? CompanyId = null);
