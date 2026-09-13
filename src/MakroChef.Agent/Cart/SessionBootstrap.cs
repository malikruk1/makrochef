using System.Text.Json;
using MakroChef.Domain.Cart;
using MakroChef.Mcp;

namespace MakroChef.Agent.Cart;

/// <summary>TASKS.md 3.3: silpo_get_my_shopping_cart -> silpo_get_shopping_cart_by_id to pull
/// branchId/deliveryType/timeslot needed by most other tools. If the guest has no cart yet, the
/// full silpo_find_address -> silpo_get_available_delivery_types -> silpo_create_shopping_cart
/// flow is NOT implemented here — it needs a real address chosen by the guest, which this
/// backend has no UI for yet (BLOCKERS.md, 2026-09-14 entry). Returns null in that case rather
/// than inventing branch/delivery data.</summary>
public class SessionBootstrap(IMakroChefMcpClient mcpClient)
{
    public async Task<SessionContext?> EnsureAsync(CancellationToken cancellationToken = default)
    {
        var myCartJson = await mcpClient.CallToolAsync("silpo_get_my_shopping_cart", new Dictionary<string, object?>(), cancellationToken);
        var myCart = JsonDocument.Parse(myCartJson).RootElement;

        if (!myCart.TryGetProperty("exists", out var existsEl) || existsEl.ValueKind != JsonValueKind.True
            || !myCart.TryGetProperty("shoppingCartId", out var cartIdEl) || cartIdEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var shoppingCartId = cartIdEl.GetString()!;
        var cartByIdJson = await mcpClient.CallToolAsync(
            "silpo_get_shopping_cart_by_id",
            new Dictionary<string, object?> { ["shoppingCartId"] = shoppingCartId },
            cancellationToken);

        var root = JsonDocument.Parse(cartByIdJson).RootElement;
        if (!root.TryGetProperty("cart", out var cart) || cart.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var deliveryType = cart.TryGetProperty("deliveryType", out var dt) && dt.ValueKind == JsonValueKind.String ? dt.GetString() : null;

        string? branchId = null;
        if (cart.TryGetProperty("shipments", out var shipments) && shipments.ValueKind == JsonValueKind.Array)
        {
            foreach (var shipment in shipments.EnumerateArray())
            {
                if (shipment.TryGetProperty("branchId", out var b) && b.ValueKind == JsonValueKind.String)
                {
                    branchId = b.GetString();
                    break;
                }
            }
        }

        string? timeslotStart = null;
        string? timeslotEnd = null;
        if (cart.TryGetProperty("timeslot", out var timeslot) && timeslot.ValueKind == JsonValueKind.Object)
        {
            timeslotStart = timeslot.TryGetProperty("start", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            timeslotEnd = timeslot.TryGetProperty("end", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        }

        if (branchId is null || deliveryType is null || timeslotStart is null || timeslotEnd is null)
        {
            return null;
        }

        return new SessionContext(shoppingCartId, branchId, deliveryType, timeslotStart, timeslotEnd);
    }
}
