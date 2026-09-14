using System.Text.Json;
using MakroChef.Agent.Cart;
using MakroChef.Agent.Catalog;
using MakroChef.Agent.Coverage;
using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Mcp.OAuth;
using MakroChef.Nutrition;

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

        // BLOCKERS.md: reoptimization isn't actually removing out-of-stock items from the real
        // cart despite silpo_remove_cart_products/silpo_add_or_update_cart_products both
        // returning success - dump the exact input schema for both so the real required
        // shape/argument names (not just prose description) can be confirmed.
        Console.WriteLine();
        Console.WriteLine("=== input schemas: silpo_remove_cart_products / silpo_add_or_update_cart_products / silpo_update_shopping_cart / silpo_add_or_update_certificates ===");
        foreach (var tool in tools.Where(t => t.Name is "silpo_remove_cart_products" or "silpo_add_or_update_cart_products" or "silpo_update_shopping_cart" or "silpo_add_or_update_certificates" or "silpo_get_similar_products" or "silpo_get_replacements"))
        {
            Console.WriteLine($"--- {tool.Name} ---");
            Console.WriteLine(tool.InputSchemaJson);
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

        // CHECKPOINTS.md technical debt: CategoryResolver matches deficit keywords against
        // silpo_get_categories' flat title list by substring, not the full get_categories_tree -
        // dump both the real input schema and a live response to see if the tree actually offers
        // something categories doesn't (parent/child structure, more precise slugs) before writing
        // code against a schema nobody has ever confirmed live.
        Console.WriteLine();
        Console.WriteLine("=== input schema: silpo_get_categories_tree ===");
        var treeTool = tools.FirstOrDefault(t => t.Name == "silpo_get_categories_tree");
        Console.WriteLine(treeTool is not null ? treeTool.InputSchemaJson : "(tool not found in tools/list)");

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_categories_tree (raw, with session args) ===");
        var categoriesTree = await client.CallToolAsync("silpo_get_categories_tree", sessionArgs);
        Console.WriteLine(categoriesTree);

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

        // BLOCKERS.md B-6: POST /api/checkout crashed live with "'E' is an invalid start of a
        // value" right after silpo_get_my_certificates succeeded (per McpCalls log) - dump it raw
        // to see the actual shape/text CheckoutCascade's unguarded JsonDocument.Parse choked on.
        // GuestContextCollector's family/restrictions field names were guessed and never
        // confirmed live - same risk class as every other bug found this session.
        Console.WriteLine();
        Console.WriteLine("=== silpo_get_my_family (raw) ===");
        var family = await client.CallToolAsync("silpo_get_my_family", new Dictionary<string, object?>());
        Console.WriteLine(family);

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_my_food_restrictions (raw) ===");
        var restrictions = await client.CallToolAsync("silpo_get_my_food_restrictions", new Dictionary<string, object?>());
        Console.WriteLine(restrictions);

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_my_premium_subscription (raw) ===");
        var premium = await client.CallToolAsync("silpo_get_my_premium_subscription", new Dictionary<string, object?>());
        Console.WriteLine(premium);

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_my_certificates (raw) ===");
        var certificates = await client.CallToolAsync("silpo_get_my_certificates", new Dictionary<string, object?>());
        Console.WriteLine(certificates);

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_promo_codes (raw) ===");
        try
        {
            var promoCodes = await client.CallToolAsync("silpo_get_promo_codes", new Dictionary<string, object?>());
            Console.WriteLine(promoCodes);
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

        // SwapGenerator/ReoptimizationService rely on JsonFieldScanner.ExtractProductSlugs (which
        // is shape-agnostic) here, so this is a lower-risk check than the others - but every
        // other "obviously fine" assumption this session turned out wrong at least once, so worth
        // one real confirmation.
        Console.WriteLine();
        Console.WriteLine($"=== silpo_get_similar_products (raw, real slug={slug}) ===");
        try
        {
            var similar = await client.CallToolAsync(
                "silpo_get_similar_products",
                new Dictionary<string, object?>(sessionArgs) { ["productId"] = sampleProductId });
            Console.WriteLine(similar);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine($"=== silpo_get_replacements (raw, real productId={sampleProductId}) ===");
        try
        {
            var replacements = await client.CallToolAsync(
                "silpo_get_replacements",
                new Dictionary<string, object?>(sessionArgs) { ["productId"] = sampleProductId });
            Console.WriteLine(replacements);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("=== silpo_get_categories (raw, no parent filter) ===");
        var categories = await client.CallToolAsync("silpo_get_categories", new Dictionary<string, object?>(sessionArgs));
        Console.WriteLine(categories);

        // BLOCKERS.md #16: GET /api/basket returns candidatePoolSize:0 on the live account even
        // with confirmed-correct nutrient keys. Reproduce CandidatePoolBuilder.ResolveCandidateAsync
        // step by step for one real deficit category so we can see exactly which step returns
        // null/empty, instead of just the aggregate MCP-call counts.
        Console.WriteLine();
        Console.WriteLine("=== silpo_get_products with a REAL category slug (\"syr-kyslomolochnyi-4988\") ===");
        var byRealSlug = await client.CallToolAsync(
            "silpo_get_products",
            new Dictionary<string, object?>(sessionArgs) { ["category"] = "syr-kyslomolochnyi-4988" });
        Console.WriteLine(byRealSlug);

        // BLOCKERS.md #18: even with a real budget and 173 candidates, the solver stays
        // infeasible. Run the actual BasketPlanner (same code /api/basket uses) and dump the
        // candidate pool's category breakdown + achievable protein vs the target, since
        // BasketSolver caps units PER category (diversity constraint) - if too few categories
        // carry protein-rich items, the cap itself can make the target unreachable regardless of
        // budget/sugar/kcal.
        Console.WriteLine();
        Console.WriteLine("=== BasketPlanner full breakdown (BLOCKERS.md #18) ===");
        var loggingSolver = new MakroChef.Agent.Tracing.LoggingBasketSolver(new MakroChef.Solver.BasketSolver(), recorder);
        var plan = await new BasketPlanner(client, loggingSolver).PlanAsync();
        if (plan is null)
        {
            Console.WriteLine("(BasketPlanner returned null - no session)");
            return 0;
        }

        Console.WriteLine($"TargetProteinMg={plan.Request.TargetProteinMg} MaxSugarMg={plan.Request.MaxSugarMg} KcalMin={plan.Request.KcalMin} KcalMax={plan.Request.KcalMax} BaselineCostKopecks={plan.Request.BaselineCostKopecks} MaxUnitsPerCategory={plan.Request.MaxUnitsPerCategory}");
        Console.WriteLine($"Candidate pool size: {plan.Request.Candidates.Count}");

        var byCategory = plan.Request.Candidates
            .Where(c => !c.Restricted)
            .GroupBy(c => c.Category)
            .Select(g => new
            {
                Category = g.Key,
                Count = g.Count(),
                MaxAchievableProteinMg = g.OrderByDescending(c => c.ProteinMg).Take(plan.Request.MaxUnitsPerCategory).Sum(c => c.ProteinMg),
            })
            .OrderByDescending(g => g.MaxAchievableProteinMg)
            .ToList();

        foreach (var g in byCategory)
        {
            Console.WriteLine($"  category=\"{g.Category}\" count={g.Count} maxAchievableProteinMg(top {plan.Request.MaxUnitsPerCategory})={g.MaxAchievableProteinMg}");
        }

        var totalMaxAchievableProteinMg = byCategory.Sum(g => g.MaxAchievableProteinMg);
        Console.WriteLine($"Sum of per-category max achievable protein: {totalMaxAchievableProteinMg}mg vs target {plan.Request.TargetProteinMg}mg -> {(totalMaxAchievableProteinMg >= plan.Request.TargetProteinMg ? "REACHABLE in principle" : "STRUCTURALLY UNREACHABLE even ignoring budget/sugar/kcal")}");

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
