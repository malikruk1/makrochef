using System.Text.Json;
using MakroChef.Domain.Cart;
using MakroChef.Mcp;

namespace MakroChef.Agent.Cart;

/// <summary>TASKS.md 7.3. 🔒 needs a live cart + real checkout (BLOCKERS.md B-2, B-6) — the
/// order of these five steps is mandatory, each one changes what the next sees.</summary>
public class CheckoutCascade(IMakroChefMcpClient mcpClient)
{
    public async Task<CheckoutLinks> RunAsync(Func<decimal, Task<bool>> confirmApplyBonus, CancellationToken cancellationToken = default)
    {
        var emptyArgs = new Dictionary<string, object?>();

        // a) Premium changes delivery terms and therefore the baseline (TASKS.md 4.3) - read
        // it first so everything downstream accounts for it.
        await mcpClient.CallToolAsync("get_my_premium_subscription", emptyArgs, cancellationToken);

        // b) Certificates
        var certificatesJson = await mcpClient.CallToolAsync("get_my_certificates", emptyArgs, cancellationToken);
        var certificateIds = ExtractIds(certificatesJson, "certificateId");
        if (certificateIds.Count > 0)
        {
            await mcpClient.CallToolAsync(
                "add_or_update_certificates",
                new Dictionary<string, object?> { ["certificateIds"] = certificateIds },
                cancellationToken);
        }

        // c) Most advantageous promo code
        var promoCodesJson = await mcpClient.CallToolAsync("get_promo_codes", emptyArgs, cancellationToken);
        var bestPromoCode = ExtractBestPromoCode(promoCodesJson);
        if (bestPromoCode is not null)
        {
            await mcpClient.CallToolAsync(
                "update_shopping_cart",
                new Dictionary<string, object?> { ["promoCode"] = bestPromoCode },
                cancellationToken);
        }

        // d) Bonus balance - ask, never apply silently
        var loyaltyJson = await mcpClient.CallToolAsync("get_loyalty_info", emptyArgs, cancellationToken);
        var (bonusAvailable, bonusRequested, isEnabled) = ParseLoyalty(loyaltyJson);
        if (bonusAvailable > 0 && bonusRequested is null && isEnabled && await confirmApplyBonus(bonusAvailable))
        {
            await mcpClient.CallToolAsync(
                "update_shopping_cart",
                new Dictionary<string, object?> { ["bonusRequested"] = bonusAvailable },
                cancellationToken);
        }

        // e) Final read -> checkout links
        var cartJson = await mcpClient.CallToolAsync("get_shopping_cart_by_id", emptyArgs, cancellationToken);
        return ExtractCheckoutLinks(cartJson);
    }

    private static List<string> ExtractIds(string json, string idKey)
    {
        var root = JsonDocument.Parse(json).RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                ? items
                : (JsonElement?)null;

        if (array is null)
        {
            return [];
        }

        var ids = new List<string>();
        foreach (var element in array.Value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var key in new[] { idKey, "id" })
            {
                if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    ids.Add(value.GetString()!);
                    break;
                }
            }
        }

        return ids;
    }

    private static string? ExtractBestPromoCode(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        if (root.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? bestCode = null;
        decimal bestDiscount = -1;

        foreach (var element in root.EnumerateArray())
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

    private static (decimal Available, decimal? Requested, bool IsEnabled) ParseLoyalty(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (0, null, false);
        }

        decimal available = 0;
        foreach (var key in new[] { "bonusAvailable", "bonusBalance", "balance" })
        {
            if (root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                available = value.GetDecimal();
                break;
            }
        }

        decimal? requested = root.TryGetProperty("bonusRequested", out var requestedValue) && requestedValue.ValueKind == JsonValueKind.Number
            ? requestedValue.GetDecimal()
            : null;

        var isEnabled = !root.TryGetProperty("isEnabled", out var enabledValue) || enabledValue.ValueKind != JsonValueKind.False;

        return (available, requested, isEnabled);
    }

    private static CheckoutLinks ExtractCheckoutLinks(string json)
    {
        var root = JsonDocument.Parse(json).RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new CheckoutLinks(null, null);
        }

        string? webLink = root.TryGetProperty("checkoutWebLink", out var web) && web.ValueKind == JsonValueKind.String ? web.GetString() : null;
        string? mobileLink = root.TryGetProperty("checkoutMobileLink", out var mobile) && mobile.ValueKind == JsonValueKind.String ? mobile.GetString() : null;

        return new CheckoutLinks(webLink, mobileLink);
    }
}
