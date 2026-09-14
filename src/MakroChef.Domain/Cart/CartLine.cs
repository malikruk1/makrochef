namespace MakroChef.Domain.Cart;

// CompanyId: confirmed live (2026-09-14) that silpo_get_replacements requires a companyId
// alongside productIds - each real cart line carries its own companyId, needed to look that up
// without an extra get_product_details round trip.
public record CartLine(string ProductId, int Quantity, string? Name = null, string? CompanyId = null);
