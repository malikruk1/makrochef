namespace MakroChef.Domain.Cart;

/// <summary>branchId/deliveryType/timeslot the API requires for most catalog and order-history
/// calls (silpo_get_product_details, silpo_find_products_batch, silpo_get_my_offline_orders,
/// ...) — confirmed live 2026-09-14, not documented anywhere in tools/list's input schemas
/// alone. Comes from the guest's existing cart (TASKS.md 3.3).</summary>
public record SessionContext(string ShoppingCartId, string BranchId, string DeliveryType, string TimeslotStart, string TimeslotEnd);
