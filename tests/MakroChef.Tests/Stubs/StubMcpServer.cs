using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MakroChef.Domain.Cart;
using ModelContextProtocol.Server;

namespace MakroChef.Tests.Stubs;

/// <summary>Shared SessionContext for tests exercising CandidatePoolBuilder/SwapGenerator/
/// ReoptimizationService/ExactMcpNutritionResolver against the stub server. Values match those
/// baked into GetShoppingCartById's stub response.</summary>
public static class StubSession
{
    public static readonly SessionContext Default = new(
        "stub-cart-1", "stub-branch-1", "SelfPickup",
        "2026-09-14T06:00:00+00:00", "2026-09-14T06:30:00+00:00");
}

[McpServerToolType]
public static class StubTools
{
    public static int GetProductDetailsCallCount;

    [McpServerTool(Name = "silpo_ping"), Description("Stub tool for gate 3.1 tests.")]
    public static string Ping(string message) => $"pong:{message}";

    [McpServerTool(Name = "silpo_get_product_details"), Description("Stub tool for gate 3.3/3.4/6 tests.")]
    public static string GetProductDetails(string? productId = null, string? slug = null)
    {
        Interlocked.Increment(ref GetProductDetailsCallCount);

        // Both calling conventions must keep working: legacy callers (CoverageProbe, not yet
        // migrated) pass productId; the SessionContext-aware pipeline (CandidatePoolBuilder,
        // SwapGenerator, ReoptimizationService, ExactMcpNutritionResolver) passes slug. In every
        // stub fixture slug == productId, so either key resolves the same fixture.
        var key = productId ?? slug ?? "";

        if (CatalogFixture.ProductDetailsByAndId.TryGetValue(key, out var fixture))
        {
            return fixture;
        }

        // Deterministic fixture for gate 3.4: "gapN" products simulate a category with
        // systematically incomplete nutrient data (e.g. weighed goods), everything else
        // has full Б/Ж/В/цукор. Shape matches the real live response (2026-09-14).
        if (key.StartsWith("gap", StringComparison.Ordinal))
        {
            return "{\"success\":true,\"product\":{\"id\":\"" + key + "\",\"category\":\"ваговий\",\"attributes\":{\"Білки (г)\":8}}}";
        }

        return "{\"success\":true,\"product\":{\"id\":\"" + key +
               "\",\"category\":\"молочні\",\"price\":25,\"attributes\":{\"Білки (г)\":10,\"Жири (г)\":5,\"Вуглеводи (г)\":12,\"У тому числі цукри (г)\":6}}}";
    }

    [McpServerTool(Name = "silpo_get_my_offline_orders"), Description("Stub fixture for gate 3.4.")]
    public static string GetMyOfflineOrders() => CoverageFixture.OfflineOrdersJson;

    [McpServerTool(Name = "silpo_get_my_online_orders"), Description("Stub fixture for gate 3.4.")]
    public static string GetMyOnlineOrders() => CoverageFixture.OnlineOrdersJson;

    [McpServerTool(Name = "silpo_get_my_profile"), Description("Stub fixture for gate 4.1.")]
    public static string GetMyProfile() => """{"success":true,"profile":{"birthday":"1995-06-15"}}""";

    // Real shape (confirmed live 2026-09-14): "members" (adult household members, self flagged
    // "itsMe":true and excluded by GuestContextCollector) and a separate "children" array - not
    // one flat "members" list with an "age" per entry.
    [McpServerTool(Name = "silpo_get_my_family"), Description("Stub fixture for gate 4.1.")]
    public static string GetMyFamily() => """{"members":[{"itsMe":true},{"age":40}],"children":[{"age":8}]}""";

    // Real shape (confirmed live 2026-09-14): "restrictions" holds objects {"slug","name"}, not
    // plain strings.
    [McpServerTool(Name = "silpo_get_my_food_restrictions"), Description("Stub fixture for gate 4.1.")]
    public static string GetMyFoodRestrictions() => """{"restrictions":[{"slug":"ryba","name":"риба"},{"slug":"horikhy","name":"горіхи"}]}""";

    [McpServerTool(Name = "silpo_get_my_delivery_addresses"), Description("Stub fixture for gate 4.1.")]
    public static string GetMyDeliveryAddresses() => """[{"city":"Київ","street":"Хрещатик"}]""";

    [McpServerTool(Name = "silpo_get_loyalty_info"), Description("Stub fixture for gate 4.1.")]
    // NOTE (BLOCKERS.md, 2026-09-14): a live call showed get_loyalty_info's real shape only
    // has loyalty.balance.total - no bonusAvailable/bonusRequested/isEnabled. Those actually
    // live in get_shopping_cart_by_id's root-level "loyalty" object instead. CheckoutCascade
    // still reads them from get_loyalty_info (open gap); this stub combines both shapes so
    // GuestContextCollector's and CheckoutCascade's tests can both pass until that's fixed.
    public static string GetLoyaltyInfo() =>
        """{"success":true,"loyalty":{"balance":{"total":275.5},"bonusAvailable":275.5,"bonusRequested":null,"isEnabled":true}}""";

    [McpServerTool(Name = "silpo_find_products_batch"), Description("Stub fixture for gate 6.1.")]
    public static string FindProductsBatch(string[]? products = null)
    {
        // Real find_products_batch echoes back slug for each requested id. Every stub fixture
        // uses slug == productId, so any id from CatalogFixture.ProductDetailsByAndId resolves.
        if (products is null || products.Length == 0)
        {
            return CatalogFixture.SeedProductsJson;
        }

        var entries = products.Select(id => "{\"productId\":\"" + id + "\",\"slug\":\"" + id + "\"}");
        return "[" + string.Join(",", entries) + "]";
    }

    [McpServerTool(Name = "silpo_get_products"), Description("Stub fixture for gate 6.1.")]
    public static string GetProducts(string category) => CatalogFixture.ProductsByCategory(category);

    // Real silpo_get_products needs a category SLUG from silpo_get_categories, not the guessed
    // free-text word (confirmed live, 2026-09-14 - see CategoryResolver). This stub's category
    // titles equal their slugs equal the deficit keywords the tests already use ("сир"/"риба"/
    // "яйця"), so CategoryResolver.ResolveSlugsAsync(keyword) resolves back to that same keyword
    // and CandidatePoolBuilderTests keep working unchanged.
    [McpServerTool(Name = "silpo_get_categories"), Description("Stub fixture for gate 6.1.")]
    public static string GetCategories() =>
        """{"success":true,"categories":[{"title":"сир","slug":"сир"},{"title":"риба","slug":"риба"},{"title":"яйця","slug":"яйця"}]}""";

    [McpServerTool(Name = "silpo_get_similar_products"), Description("Stub fixture for gate 6.2.")]
    public static string GetSimilarProducts(string productId) => CatalogFixture.SimilarProducts(productId);

    [McpServerTool(Name = "silpo_get_replacements"), Description("Stub fixture for gate 7.2.")]
    public static string GetReplacements(string productId) => productId switch
    {
        "test_cheese" => """[{"productId":"cheese_b","slug":"cheese_b"}]""",
        _ => "[]",
    };

    [McpServerTool(Name = "silpo_clear_shopping_cart"), Description("Stub cart for gate 7.")]
    public static string ClearShoppingCart()
    {
        StubCartState.Lines.Clear();
        return """{"success":true}""";
    }

    [McpServerTool(Name = "silpo_add_or_update_cart_products"), Description("Stub cart for gate 7.")]
    // Real schema (confirmed live 2026-09-14, tools/list inputSchema): the array param is named
    // "products" (not "items"), each entry requires productId+companyId+branchId+quantity.
    public static string AddOrUpdateCartProducts(System.Text.Json.JsonElement products)
    {
        foreach (var item in products.EnumerateArray())
        {
            var productId = item.GetProperty("productId").GetString()!;
            var quantity = item.TryGetProperty("quantity", out var q) ? q.GetInt32() : 1;
            StubCartState.Lines[productId] = quantity;
        }

        return """{"success":true}""";
    }

    // Real schema (confirmed live 2026-09-14): the array param is named "products" (not
    // "productIds"), and each entry is an object {"productId": "..."} (not a bare string).
    [McpServerTool(Name = "silpo_remove_cart_products"), Description("Stub cart for gate 7.")]
    public static string RemoveCartProducts(System.Text.Json.JsonElement products)
    {
        foreach (var item in products.EnumerateArray())
        {
            StubCartState.Lines.Remove(item.GetProperty("productId").GetString()!);
        }

        return """{"success":true}""";
    }

    [McpServerTool(Name = "silpo_get_shopping_cart_by_id"), Description("Stub cart for gate 7.")]
    public static string GetShoppingCartById()
    {
        var products = StubCartState.Lines.Select(kv => $$"""{"productId":"{{kv.Key}}","quantity":{{kv.Value}}}""");
        var validations = StubCartState.Lines.Keys
            .Where(id => StubCartState.OutOfStock.Contains(id))
            .Select(id => "{\"message\":\"product.offer.status.not_available\",\"context\":{\"productId\":\"" + id + "\"}}");

        // Real shape (confirmed live 2026-09-14): wrapped in "cart", shipments[].products[],
        // calculation.validations[]/totalAfterDiscounts. branchId/deliveryType/timeslot are
        // fixed stub values for SessionBootstrap tests (gate 3.3). Root-level "loyalty" (not
        // checkoutWebLink/checkoutMobileLink, which no real cart ever carries - B-6, 2026-09-14)
        // is where the real bonusAvailable number lives; CheckoutCascade reads it from here.
        // "address" included because silpo_update_shopping_cart requires it (alongside
        // deliveryType/timeslot/shipments) verbatim from the cart on every call - confirmed via
        // its real input schema (2026-09-14); CheckoutCascade now copies all four through.
        return "{\"cart\":{\"id\":\"stub-cart-1\",\"deliveryType\":\"SelfPickup\"," +
               "\"timeslot\":{\"start\":\"2026-09-14T06:00:00+00:00\",\"end\":\"2026-09-14T06:30:00+00:00\"}," +
               "\"address\":{\"addressType\":\"self-pickup\",\"latitude\":\"50.0\",\"longitude\":\"30.0\"}," +
               "\"shipments\":[{\"branchId\":\"stub-branch-1\",\"companyId\":\"stub-company-1\",\"products\":[" + string.Join(",", products) + "]}]," +
               "\"calculation\":{\"totalAfterDiscounts\":0,\"validations\":[" + string.Join(",", validations) + "]}}," +
               "\"loyalty\":{\"bonusAvailable\":275.5,\"bonusRequested\":null,\"isEnabled\":true}}";
    }

    [McpServerTool(Name = "silpo_get_my_shopping_cart"), Description("Stub cart for gate 3.3.")]
    public static string GetMyShoppingCart() => """{"success":true,"shoppingCartId":"stub-cart-1","exists":true}""";

    [McpServerTool(Name = "silpo_get_my_premium_subscription"), Description("Stub fixture for gate 7.3.")]
    public static string GetMyPremiumSubscription() => """{"isPremium":false}""";

    [McpServerTool(Name = "silpo_get_my_certificates"), Description("Stub fixture for gate 7.3.")]
    // Real key is "barcode" (confirmed via silpo_add_or_update_certificates' input schema,
    // 2026-09-14), not the guessed "certificateId".
    public static string GetMyCertificates() => """[{"barcode":"cert-barcode-1"}]""";

    [McpServerTool(Name = "silpo_add_or_update_certificates"), Description("Stub cart for gate 7.3.")]
    public static string AddOrUpdateCertificates(object? certificatesToAdd = null) { StubCartState.CheckoutReady = true; return """{"success":true}"""; }

    [McpServerTool(Name = "silpo_get_promo_codes"), Description("Stub fixture for gate 7.3.")]
    // Real shape (confirmed live 2026-09-14): wrapped in "promoCodes", not a bare array.
    public static string GetPromoCodes() => """{"success":true,"promoCodes":[{"code":"SAVE5","discountAmount":5},{"code":"SAVE20","discountAmount":20}]}""";

    [McpServerTool(Name = "silpo_update_shopping_cart"), Description("Stub cart for gate 7.3.")]
    public static string UpdateShoppingCart() => """{"success":true}""";
}

/// <summary>In-memory cart for gate 7 tests (7.1/7.2). Reset per test via <see cref="Lines"/>
/// and <see cref="OutOfStock"/> since it's static, shared MCP-server-wide state.</summary>
public static class StubCartState
{
    public static readonly Dictionary<string, int> Lines = new();
    public static readonly HashSet<string> OutOfStock = new();

    /// <summary>Flips true once add_or_update_certificates runs, so get_shopping_cart_by_id's
    /// checkout links only appear at the end of the 7.3 cascade, not before.</summary>
    public static bool CheckoutReady;
}

/// <summary>Catalog fixture for gate 6 (candidate pool + swaps). Prices in currency units
/// (parsed as kopecks by ProductDetailsParser), nutrients in grams per 100g.</summary>
public static class CatalogFixture
{
    public const string SeedProductsJson = """[{"productId":"yogurt_x","slug":"yogurt_x"},{"productId":"cheese_a","slug":"cheese_a"}]""";

    public static string ProductsByCategory(string category) => category switch
    {
        "сир" => """[{"productId":"cheese_a","slug":"cheese_a"},{"productId":"cheese_b","slug":"cheese_b"}]""",
        "риба" => """[{"productId":"fish_a","slug":"fish_a"}]""",
        "яйця" => """[{"productId":"eggs_a","slug":"eggs_a"}]""",
        _ => "[]",
    };

    public static string SimilarProducts(string productId) => productId switch
    {
        "yogurt_x" => """[{"productId":"yogurt_y","slug":"yogurt_y"},{"productId":"yogurt_z","slug":"yogurt_z"}]""",
        "bread_x" => """[{"productId":"bread_y","slug":"bread_y"}]""",
        "milk_x" => """[{"productId":"milk_y","slug":"milk_y"}]""",
        "juice_x" => """[{"productId":"juice_y","slug":"juice_y"}]""",
        "cereal_x" => """[{"productId":"cereal_y","slug":"cereal_y"}]""",
        _ => "[]",
    };

    /// <summary>get_product_details fixtures for gate 6: five "_x -> _y" pairs are a real
    /// improvement (more protein or less sugar, cheaper), "_z" is worse both ways so the swap
    /// generator must reject it. Shape matches the real live response (2026-09-14): wrapped in
    /// "product", nutrients under Ukrainian-keyed "attributes", weight from "displayRatio".</summary>
    public static readonly Dictionary<string, string> ProductDetailsByAndId = new()
    {
        ["yogurt_x"] = Product("молочні", 30, protein: 4, sugar: 12),
        ["yogurt_y"] = Product("молочні", 27, protein: 6, sugar: 8),
        ["yogurt_z"] = Product("молочні", 32, protein: 3, sugar: 15),
        ["bread_x"] = Product("випічка", 25, protein: 8, sugar: 5),
        ["bread_y"] = Product("випічка", 24, protein: 9, sugar: 4),
        ["milk_x"] = Product("молочні", 20, protein: 3, sugar: 5),
        ["milk_y"] = Product("молочні", 19, protein: 3.5m, sugar: 4),
        ["juice_x"] = Product("напої", 35, protein: 0, sugar: 20),
        ["juice_y"] = Product("напої", 34, protein: 0.5m, sugar: 15),
        ["cereal_x"] = Product("крупи", 40, protein: 8, sugar: 10),
        ["cereal_y"] = Product("крупи", 38, protein: 10, sugar: 6),
        ["cheese_a"] = Product("сир", 80, protein: 25, sugar: 1, weightGrams: 200),
        ["cheese_b"] = Product("сир", 75, protein: 22, sugar: 1, weightGrams: 200),
        ["fish_a"] = Product("риба", 120, protein: 20, sugar: 0, weightGrams: 300),
        ["eggs_a"] = Product("яйця", 45, protein: 13, sugar: 0.5m, weightGrams: 600),
    };

    private static string Product(string category, decimal price, decimal protein, decimal sugar, int weightGrams = 100) =>
        "{\"success\":true,\"product\":{\"category\":\"" + category + "\",\"price\":" + price.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        ",\"companyId\":\"stub-company-1\",\"displayRatio\":\"" + weightGrams + "г\",\"attributes\":{\"Білки (г)\":" + protein.ToString(System.Globalization.CultureInfo.InvariantCulture) +
        ",\"У тому числі цукри (г)\":" + sugar.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}}";
}

/// <summary>20-SKU fixture for gate 3.4 (15 complete + 5 "gap" products), split across two
/// weeks of orders so the median-weekly-receipt calculation has something to chew on.</summary>
public static class CoverageFixture
{
    // Real shape (confirmed live 2026-09-14): wrapped in "orders", offline per-order total is
    // "sumReg" (not "totalAmount"), line items live under "products" (not "items").
    public const string OfflineOrdersJson = """
        {"success":true,"orders":[
          {"sumReg": 850.50, "createdAt": "2026-08-03T10:00:00Z",
           "products": [
             {"productId":"sku1"},{"productId":"sku2"},{"productId":"sku3"},
             {"productId":"sku4"},{"productId":"sku5"},{"productId":"gap1"},{"productId":"gap2"}
           ]},
          {"sumReg": 920.00, "createdAt": "2026-08-10T10:00:00Z",
           "products": [
             {"productId":"sku6"},{"productId":"sku7"},{"productId":"sku8"},
             {"productId":"sku9"},{"productId":"sku10"},{"productId":"gap3"}
           ]}
        ]}
        """;

    // Real shape (confirmed live 2026-09-14): wrapped in "orders", online per-order total is
    // "amount", line items live under "products" (not "items").
    public const string OnlineOrdersJson = """
        {"success":true,"orders":[
          {"amount": 430.25, "createdAt": "2026-08-10T18:00:00Z",
           "products": [
             {"productId":"sku11"},{"productId":"sku12"},{"productId":"sku13"},
             {"productId":"sku14"},{"productId":"sku15"},{"productId":"gap4"},{"productId":"gap5"}
           ]}
        ]}
        """;
}

/// <summary>An in-process MCP server (real SDK, no HTTP mocking) used only so gate 3.1 can
/// prove the client reads tools/list correctly, without a live Silpo account.</summary>
public sealed class StubMcpServer : IAsyncDisposable
{
    private WebApplication? _app;

    public Uri Endpoint { get; private set; } = null!;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly(typeof(StubTools).Assembly);

        _app = builder.Build();
        _app.Urls.Add("http://127.0.0.1:0");
        _app.MapMcp();

        await _app.StartAsync();

        var address = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
            .Addresses.First();

        Endpoint = new Uri(address);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
