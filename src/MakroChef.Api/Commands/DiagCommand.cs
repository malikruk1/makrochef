using System.Text.Json;
using MakroChef.Agent.Cart;
using MakroChef.Agent.Coverage;
using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Mcp.OAuth;

namespace MakroChef.Api.Commands;

/// <summary>Temporary: `dotnet run -- diag` prints raw MCP tool responses so we can see the
/// real payload shape against a live account, instead of guessing from tools/list input
/// schemas alone. Remove once the real schema is confirmed and parsers are adjusted.</summary>
public static class DiagCommand
{
    public static async Task<int> RunAsync(Uri mcpBaseUri, Guid userId, MakroChefDbContext db, EfMcpTokenStore tokenStore, TokenEncryptor tokenEncryptor)
    {
        var stored = await tokenStore.FindByUserAsync(userId);
        if (stored is null)
        {
            Console.Error.WriteLine("Немає токена.");
            return 1;
        }

        var accessToken = tokenEncryptor.Decrypt(new EncryptedToken(stored.EncryptedAccessToken, stored.AccessTokenNonce));
        var tokenProvider = new StaticTokenProvider(accessToken);
        var recorder = new EfMcpCallRecorder(db);
        await using var client = new MakroChefMcpClient(mcpBaseUri, tokenProvider, recorder, userId);

        Console.WriteLine("=== tools/list ===");
        var tools = await client.ListToolsAsync();
        foreach (var tool in tools)
        {
            Console.WriteLine($"- {tool.Name}: {tool.Description}");
        }

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_my_shopping_cart (raw) ===");
        var myCart = await client.CallToolAsync("silpo_get_my_shopping_cart", new Dictionary<string, object?>());
        Console.WriteLine(myCart);

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_shopping_cart_by_id (raw) ===");
        var myCartParsed = System.Text.Json.JsonDocument.Parse(myCart).RootElement;
        if (myCartParsed.TryGetProperty("shoppingCartId", out var cartIdEl) && cartIdEl.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var cartById = await client.CallToolAsync(
                "silpo_get_shopping_cart_by_id",
                new Dictionary<string, object?> { ["shoppingCartId"] = cartIdEl.GetString() });
            Console.WriteLine(cartById);
        }
        else
        {
            Console.WriteLine("(no cart id)");
        }

        // B-5: don't hardcode branchId/slug from a stale prior session - bootstrap a real one
        // and pull a real historical product id, so this dump reflects the account's actual
        // current session and an actual purchased SKU, not a possibly-expired one.
        Console.WriteLine();
        Console.WriteLine("=== SessionBootstrap ===");
        var session = await new SessionBootstrap(client).EnsureAsync();
        if (session is null)
        {
            Console.WriteLine("(no cart/session available - can't continue product-details diag)");
            return 0;
        }

        Console.WriteLine($"branchId={session.BranchId} deliveryType={session.DeliveryType} timeslot={session.TimeslotStart}..{session.TimeslotEnd}");

        var sessionArgs = new Dictionary<string, object?>
        {
            ["branchId"] = session.BranchId,
            ["deliveryType"] = session.DeliveryType,
            ["timeslotStart"] = session.TimeslotStart,
            ["timeslotEnd"] = session.TimeslotEnd,
        };

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_my_offline_orders (raw, with session args) ===");
        var offline = await client.CallToolAsync("silpo_get_my_offline_orders", sessionArgs);
        Console.WriteLine(offline);

        var productIds = JsonFieldScanner.ExtractProductIds(offline);
        var sampleProductId = productIds.FirstOrDefault();
        if (sampleProductId is null)
        {
            Console.WriteLine("(no product ids found in offline orders - can't continue product-details diag)");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"=== silpo_find_products_batch (raw, real historical productId={sampleProductId}) ===");
        var batchArgs = new Dictionary<string, object?>(sessionArgs) { ["products"] = new[] { sampleProductId } };
        var batch = await client.CallToolAsync("silpo_find_products_batch", batchArgs);
        Console.WriteLine(batch);

        var slug = JsonFieldScanner.ExtractProductSlugs(batch).GetValueOrDefault(sampleProductId) ?? sampleProductId;

        Console.WriteLine();
        Console.WriteLine($"=== silpo_get_product_details (raw, real slug={slug}) ===");
        var detailsArgs = new Dictionary<string, object?>(sessionArgs) { ["slug"] = slug };
        try
        {
            var details = await client.CallToolAsync("silpo_get_product_details", detailsArgs);
            Console.WriteLine(details);

            Console.WriteLine();
            Console.WriteLine("=== product.attributes keys (this is what B-5 needs) ===");
            PrintAttributeKeys(details);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_my_profile (raw) ===");
        var profile = await client.CallToolAsync("silpo_get_my_profile", new Dictionary<string, object?>());
        Console.WriteLine(profile);

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_loyalty_info (raw) ===");
        var loyalty = await client.CallToolAsync("silpo_get_loyalty_info", new Dictionary<string, object?>());
        Console.WriteLine(loyalty);

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_my_delivery_addresses (raw) ===");
        var addresses = await client.CallToolAsync("silpo_get_my_delivery_addresses", new Dictionary<string, object?>());
        Console.WriteLine(addresses);

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_my_online_orders (raw, with session args) ===");
        try
        {
            var online = await client.CallToolAsync("silpo_get_my_online_orders", sessionArgs);
            Console.WriteLine(online);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }

        return 0;
    }

    /// <summary>B-5: dump the exact attribute keys/values a real product carries so
    /// ExactMcpNutritionResolver.ParseNutrients can match them precisely instead of guessing
    /// substrings ("цукри" was an unconfirmed guess - see BLOCKERS.md #11).</summary>
    private static void PrintAttributeKeys(string productDetailsJson)
    {
        var root = JsonDocument.Parse(productDetailsJson).RootElement;
        var product = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("product", out var p) && p.ValueKind == JsonValueKind.Object
            ? p
            : root;

        if (product.ValueKind != JsonValueKind.Object || !product.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
        {
            Console.WriteLine("(no product.attributes object found)");
            return;
        }

        foreach (var prop in attributes.EnumerateObject())
        {
            Console.WriteLine($"  \"{prop.Name}\" = {prop.Value}");
        }
    }

    private class StaticTokenProvider(string token) : IMcpAuthTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(token);

        public Task<string?> ForceRefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
