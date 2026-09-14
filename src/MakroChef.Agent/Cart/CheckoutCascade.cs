using System.Text.Json;
using MakroChef.Domain.Cart;
using MakroChef.Mcp;

namespace MakroChef.Agent.Cart;

/// <summary>TASKS.md 7.3. 🔒 needs a live cart + real checkout (BLOCKERS.md B-2, B-6) — the
/// order of these five steps is mandatory, each one changes what the next sees.
///
/// Confirmed live (2026-09-14): silpo_get_shopping_cart_by_id requires shoppingCartId (same
/// discovery as BasketAssembler/CoverageProbe) — the final read here called it with no arguments
/// and would fail validation on any real cart. silpo_update_shopping_cart is cart-scoped the same
/// way, so shoppingCartId is threaded through that call too.
///
/// B-6 human eye-check done live (2026-09-14): (1) silpo_get_loyalty_info's bonusAvailable never
/// actually exists there (confirmed shape is only loyalty.balance.total) — the REAL bonusAvailable
/// number lives on get_shopping_cart_by_id's own root-level "loyalty" object
/// (<c>{"cart":{...},"loyalty":{"bonusAvailable":8.09,...}}</c>). Fixed by reading bonus info from
/// the cart response instead. (2) There is no checkoutWebLink/checkoutMobileLink anywhere in a
/// real cart response, and no silpo_checkout/place_order/pay tool exists in tools/list at all —
/// that field was an invented guess, never confirmed. The MCP surface can prepare and validate a
/// cart but cannot hand back a magic checkout link; CheckoutLinks now honestly carries the cart's
/// blocking Validations instead so the guest/UI knows to finish payment in the Silpo app itself.
///
/// Confirmed live (2026-09-14) via the tools' own JSON input schema: silpo_update_shopping_cart
/// REQUIRES deliveryType+timeslot+address+shipments on every call (not just shoppingCartId+the
/// one field being changed, as originally assumed) — its own description says to copy these
/// verbatim from the current cart response, never construct them. Every previous promo/bonus call
/// here omitted them and would fail validation (or be silently rejected), so the promo code and
/// bonus were likely never actually applied. Also: silpo_add_or_update_certificates takes
/// certificatesToAdd:[{barcode,pincode?}], not the guessed certificateIds:[...] shape.</summary>
public class CheckoutCascade(IMakroChefMcpClient mcpClient, SessionContext session)
{
    public async Task<CheckoutLinks> RunAsync(Func<decimal, Task<bool>> confirmApplyBonus, CancellationToken cancellationToken = default)
    {
        var emptyArgs = new Dictionary<string, object?>();
        var cartArgs = new Dictionary<string, object?> { ["shoppingCartId"] = session.ShoppingCartId };

        // a) Premium changes delivery terms and therefore the baseline (TASKS.md 4.3) - read
        // it first so everything downstream accounts for it.
        await mcpClient.CallToolAsync("silpo_get_my_premium_subscription", emptyArgs, cancellationToken);

        // Fetch once, up front: silpo_update_shopping_cart requires deliveryType/timeslot/
        // address/shipments verbatim on every call (confirmed via its real input schema), and the
        // real bonusAvailable also lives here (root "loyalty"), not in get_loyalty_info.
        var initialCartJson = await mcpClient.CallToolAsync("silpo_get_shopping_cart_by_id", cartArgs, cancellationToken);
        var cartFields = ExtractRequiredCartFields(initialCartJson);

        // b) Certificates - real shape is certificatesToAdd:[{barcode,pincode?}], not
        // certificateIds:[...] as originally guessed.
        var certificatesJson = await mcpClient.CallToolAsync("silpo_get_my_certificates", emptyArgs, cancellationToken);
        var barcodes = ExtractCertificateBarcodes(certificatesJson);
        if (barcodes.Count > 0)
        {
            await mcpClient.CallToolAsync(
                "silpo_add_or_update_certificates",
                new Dictionary<string, object?>
                {
                    ["shoppingCartId"] = session.ShoppingCartId,
                    ["certificatesToAdd"] = barcodes.Select(b => new Dictionary<string, object?> { ["barcode"] = b }).ToList(),
                },
                cancellationToken);
        }

        // c) Most advantageous promo code
        var promoCodesJson = await mcpClient.CallToolAsync("silpo_get_promo_codes", emptyArgs, cancellationToken);
        var bestPromoCode = ExtractBestPromoCode(promoCodesJson);
        if (bestPromoCode is not null && cartFields is not null)
        {
            await mcpClient.CallToolAsync(
                "silpo_update_shopping_cart",
                cartFields.ToArgs(session.ShoppingCartId, ("promoCode", bestPromoCode)),
                cancellationToken);
        }

        // d) Bonus balance - ask, never apply silently.
        var (bonusAvailable, bonusRequested, isEnabled) = ParseLoyalty(initialCartJson);
        if (bonusAvailable > 0 && bonusRequested is null && isEnabled && cartFields is not null && await confirmApplyBonus(bonusAvailable))
        {
            await mcpClient.CallToolAsync(
                "silpo_update_shopping_cart",
                cartFields.ToArgs(session.ShoppingCartId, ("bonusRequested", bonusAvailable)),
                cancellationToken);
        }

        // e) Final read -> real cart total + any blocking validations (no real checkout link
        // exists to hand back - see class summary).
        var cartJson = await mcpClient.CallToolAsync("silpo_get_shopping_cart_by_id", cartArgs, cancellationToken);
        var cartState = CartResponseParser.Parse(cartJson);
        return new CheckoutLinks(
            WebLink: null,
            MobileLink: null,
            TotalKopecks: cartState.TotalKopecks,
            BlockingValidations: cartState.Validations.Select(v => v.Reason).Distinct().ToList());
    }

    /// <summary>silpo_update_shopping_cart requires these four fields verbatim from the current
    /// cart on every call - null if the cart response didn't have all of them (e.g. malformed/
    /// unexpected shape), in which case promo/bonus steps are skipped rather than sending a call
    /// guaranteed to fail validation.</summary>
    private sealed record RequiredCartFields(JsonElement DeliveryType, JsonElement Timeslot, JsonElement Address, JsonElement Shipments)
    {
        public Dictionary<string, object?> ToArgs(string shoppingCartId, (string Key, object? Value) extra) => new()
        {
            ["shoppingCartId"] = shoppingCartId,
            ["deliveryType"] = DeliveryType,
            ["timeslot"] = Timeslot,
            ["address"] = Address,
            ["shipments"] = Shipments,
            [extra.Key] = extra.Value,
        };
    }

    private static RequiredCartFields? ExtractRequiredCartFields(string cartJson)
    {
        var root = JsonDocument.Parse(cartJson).RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("cart", out var cart) || cart.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!cart.TryGetProperty("deliveryType", out var deliveryType) || deliveryType.ValueKind != JsonValueKind.String
            || !cart.TryGetProperty("timeslot", out var timeslot) || timeslot.ValueKind != JsonValueKind.Object
            || !cart.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object
            || !cart.TryGetProperty("shipments", out var shipments) || shipments.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return new RequiredCartFields(deliveryType, timeslot, address, shipments);
    }

    /// <summary>Real add_or_update_certificates shape needs "barcode" (confirmed via its input
    /// schema), not the guessed "certificateId".</summary>
    private static List<string> ExtractCertificateBarcodes(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("certificates", out var certs) && certs.ValueKind == JsonValueKind.Array
                ? certs
                : (JsonElement?)null;

        if (array is null)
        {
            return [];
        }

        var barcodes = new List<string>();
        foreach (var element in array.Value.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("barcode", out var barcode) && barcode.ValueKind == JsonValueKind.String)
            {
                barcodes.Add(barcode.GetString()!);
            }
        }

        return barcodes;
    }

    // Confirmed live (2026-09-14): silpo_get_promo_codes wraps its array as
    // {"success":true,"promoCodes":[...],"meta":{...}}, not a bare array as originally assumed -
    // ExtractBestPromoCode always returned null against a real response, so a promo code would
    // never actually get applied even when one was available.
    private static string? ExtractBestPromoCode(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("promoCodes", out var p) && p.ValueKind == JsonValueKind.Array
                ? p
                : (JsonElement?)null;

        if (array is null)
        {
            return null;
        }

        string? bestCode = null;
        decimal bestDiscount = -1;

        foreach (var element in array.Value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!element.TryGetProperty("code", out var codeValue) || codeValue.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var discount = 0m;
            foreach (var key in new[] { "discountAmount", "discountPercent", "discount" })
            {
                if (element.TryGetProperty(key, out var discountValue) && discountValue.ValueKind == JsonValueKind.Number)
                {
                    discount = discountValue.GetDecimal();
                    break;
                }
            }

            if (discount > bestDiscount)
            {
                bestDiscount = discount;
                bestCode = codeValue.GetString();
            }
        }

        return bestCode;
    }

    /// <summary>Confirmed live (2026-09-14): the real bonusAvailable number lives on
    /// get_shopping_cart_by_id's root-level "loyalty" object
    /// (<c>{"cart":{...},"loyalty":{"bonusAvailable":8.09,"bonusTotal":8.09,"bonusRequested":null,"isEnabled":true}}</c>),
    /// not inside get_loyalty_info's response (that one only ever carries loyalty.balance.total -
    /// no bonusAvailable field at all). Reads the cart's root object, not get_loyalty_info's.</summary>
    private static (decimal Available, decimal? Requested, bool IsEnabled) ParseLoyalty(string cartJson)
    {
        var root = JsonDocument.Parse(cartJson).RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("loyalty", out var loyalty) || loyalty.ValueKind != JsonValueKind.Object)
        {
            return (0, null, false);
        }

        decimal available = 0;
        foreach (var key in new[] { "bonusAvailable", "bonusTotal", "bonusBalance", "balance" })
        {
            if (loyalty.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                available = value.GetDecimal();
                break;
            }
        }

        decimal? requested = loyalty.TryGetProperty("bonusRequested", out var requestedValue) && requestedValue.ValueKind == JsonValueKind.Number
            ? requestedValue.GetDecimal()
            : null;

        var isEnabled = !loyalty.TryGetProperty("isEnabled", out var enabledValue) || enabledValue.ValueKind != JsonValueKind.False;

        return (available, requested, isEnabled);
    }
}
