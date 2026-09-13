using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace MakroChef.Tests.Stubs;

[McpServerToolType]
public static class StubTools
{
    public static int GetProductDetailsCallCount;

    [McpServerTool(Name = "silpo_ping"), Description("Stub tool for gate 3.1 tests.")]
    public static string Ping(string message) => $"pong:{message}";

    [McpServerTool(Name = "silpo_get_product_details"), Description("Stub tool for gate 3.3/3.4/6 tests.")]
    public static string GetProductDetails(string productId)
    {
        Interlocked.Increment(ref GetProductDetailsCallCount);

        if (CatalogFixture.ProductDetailsByAndId.TryGetValue(productId, out var fixture))
        {
            return fixture;
        }

        // Deterministic fixture for gate 3.4: "gapN" products simulate a category with
        // systematically incomplete nutrient data (e.g. weighed goods), everything else
        // has full Б/Ж/В/цукор.
        if (productId.StartsWith("gap", StringComparison.Ordinal))
        {
            return $$"""{"productId":"{{productId}}","category":"ваговий","proteinPer100g":8}""";
        }

        return $$"""
            {"productId":"{{productId}}","category":"молочні","price":25,"proteinPer100g":10,"fatPer100g":5,"carbsPer100g":12,"sugarPer100g":6}
            """;
    }

    [McpServerTool(Name = "silpo_get_my_offline_orders"), Description("Stub fixture for gate 3.4.")]
    public static string GetMyOfflineOrders() => CoverageFixture.OfflineOrdersJson;

    [McpServerTool(Name = "silpo_get_my_online_orders"), Description("Stub fixture for gate 3.4.")]
    public static string GetMyOnlineOrders() => CoverageFixture.OnlineOrdersJson;

    [McpServerTool(Name = "silpo_get_my_profile"), Description("Stub fixture for gate 4.1.")]
    public static string GetMyProfile() => """{"birthDate":"1995-06-15T00:00:00Z"}""";

    [McpServerTool(Name = "silpo_get_my_family"), Description("Stub fixture for gate 4.1.")]
    public static string GetMyFamily() => """{"members":[{"age":8},{"age":40}]}""";

    [McpServerTool(Name = "silpo_get_my_food_restrictions"), Description("Stub fixture for gate 4.1.")]
    public static string GetMyFoodRestrictions() => """{"restrictions":["риба","горіхи"]}""";

    [McpServerTool(Name = "silpo_get_my_delivery_addresses"), Description("Stub fixture for gate 4.1.")]
    public static string GetMyDeliveryAddresses() => """[{"city":"Київ","street":"Хрещатик"}]""";

    [McpServerTool(Name = "silpo_get_loyalty_info"), Description("Stub fixture for gate 4.1.")]
    public static string GetLoyaltyInfo() => """{"bonusBalance":275.5}""";

    [McpServerTool(Name = "silpo_find_products_batch"), Description("Stub fixture for gate 6.1.")]
    public static string FindProductsBatch() => CatalogFixture.SeedProductsJson;

    [McpServerTool(Name = "silpo_get_products"), Description("Stub fixture for gate 6.1.")]
    public static string GetProducts(string category) => CatalogFixture.ProductsByCategory(category);

    [McpServerTool(Name = "silpo_get_similar_products"), Description("Stub fixture for gate 6.2.")]
    public static string GetSimilarProducts(string productId) => CatalogFixture.SimilarProducts(productId);

    [McpServerTool(Name = "silpo_get_replacements"), Description("Stub fixture for gate 7.2.")]
    public static string GetReplacements(string productId) => productId switch
    {
        "test_cheese" => """[{"productId":"cheese_b"}]""",
        _ => "[]",
    };

    [McpServerTool(Name = "silpo_clear_shopping_cart"), Description("Stub cart for gate 7.")]
    public static string ClearShoppingCart()
    {
        StubCartState.Lines.Clear();
        return """{"success":true}""";
    }

    [McpServerTool(Name = "silpo_add_or_update_cart_products"), Description("Stub cart for gate 7.")]
    public static string AddOrUpdateCartProducts(System.Text.Json.JsonElement items)
    {
        foreach (var item in items.EnumerateArray())
        {
            var productId = item.GetProperty("productId").GetString()!;
            var quantity = item.TryGetProperty("quantity", out var q) ? q.GetInt32() : 1;
            StubCartState.Lines[productId] = quantity;
        }

        return """{"success":true}""";
    }

    [McpServerTool(Name = "silpo_remove_cart_products"), Description("Stub cart for gate 7.")]
    public static string RemoveCartProducts(string[] productIds)
    {
        foreach (var id in productIds)
        {
            StubCartState.Lines.Remove(id);
        }

        return """{"success":true}""";
    }

    [McpServerTool(Name = "silpo_get_shopping_cart_by_id"), Description("Stub cart for gate 7.")]
    public static string GetShoppingCartById()
    {
        var items = StubCartState.Lines.Select(kv => $$"""{"productId":"{{kv.Key}}","quantity":{{kv.Value}}}""");
        var validations = StubCartState.Lines.Keys
            .Where(id => StubCartState.OutOfStock.Contains(id))
            .Select(id => $$"""{"productId":"{{id}}","reason":"out of stock"}""");

        var links = StubCartState.CheckoutReady
            ? ",\"checkoutWebLink\":\"https://silpo.ua/checkout/abc\",\"checkoutMobileLink\":\"silpo://checkout/abc\""
            : "";

        return $$"""{"items":[{{string.Join(",", items)}}],"validations":[{{string.Join(",", validations)}}],"totalAmount":0{{links}}}""";
    }

    [McpServerTool(Name = "silpo_get_my_premium_subscription"), Description("Stub fixture for gate 7.3.")]
    public static string GetMyPremiumSubscription() => """{"isPremium":false}""";

    [McpServerTool(Name = "silpo_get_my_certificates"), Description("Stub fixture for gate 7.3.")]
    public static string GetMyCertificates() => """[{"certificateId":"cert-1"}]""";

    [McpServerTool(Name = "silpo_add_or_update_certificates"), Description("Stub cart for gate 7.3.")]
    public static string AddOrUpdateCertificates() { StubCartState.CheckoutReady = true; return """{"success":true}"""; }

    [McpServerTool(Name = "silpo_get_promo_codes"), Description("Stub fixture for gate 7.3.")]
    public static string GetPromoCodes() => """[{"code":"SAVE5","discountAmount":5},{"code":"SAVE20","discountAmount":20}]""";

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
    public const string SeedProductsJson = """[{"productId":"yogurt_x"},{"productId":"cheese_a"}]""";

    public static string ProductsByCategory(string category) => category switch
    {
        "сир" => """[{"productId":"cheese_a"},{"productId":"cheese_b"}]""",
        "риба" => """[{"productId":"fish_a"}]""",
        "яйця" => """[{"productId":"eggs_a"}]""",
        _ => "[]",
    };

    public static string SimilarProducts(string productId) => productId switch
    {
        "yogurt_x" => """[{"productId":"yogurt_y"},{"productId":"yogurt_z"}]""",
        "bread_x" => """[{"productId":"bread_y"}]""",
        "milk_x" => """[{"productId":"milk_y"}]""",
        "juice_x" => """[{"productId":"juice_y"}]""",
        "cereal_x" => """[{"productId":"cereal_y"}]""",
        _ => "[]",
    };

    /// <summary>get_product_details fixtures for gate 6: five "_x -> _y" pairs are a real
    /// improvement (more protein or less sugar, cheaper), "_z" is worse both ways so the swap
    /// generator must reject it.</summary>
    public static readonly Dictionary<string, string> ProductDetailsByAndId = new()
    {
        ["yogurt_x"] = """{"category":"молочні","price":30,"proteinPer100g":4,"sugarPer100g":12}""",
        ["yogurt_y"] = """{"category":"молочні","price":27,"proteinPer100g":6,"sugarPer100g":8}""",
        ["yogurt_z"] = """{"category":"молочні","price":32,"proteinPer100g":3,"sugarPer100g":15}""",
        ["bread_x"] = """{"category":"випічка","price":25,"proteinPer100g":8,"sugarPer100g":5}""",
        ["bread_y"] = """{"category":"випічка","price":24,"proteinPer100g":9,"sugarPer100g":4}""",
        ["milk_x"] = """{"category":"молочні","price":20,"proteinPer100g":3,"sugarPer100g":5}""",
        ["milk_y"] = """{"category":"молочні","price":19,"proteinPer100g":3.5,"sugarPer100g":4}""",
        ["juice_x"] = """{"category":"напої","price":35,"proteinPer100g":0,"sugarPer100g":20}""",
        ["juice_y"] = """{"category":"напої","price":34,"proteinPer100g":0.5,"sugarPer100g":15}""",
        ["cereal_x"] = """{"category":"крупи","price":40,"proteinPer100g":8,"sugarPer100g":10}""",
        ["cereal_y"] = """{"category":"крупи","price":38,"proteinPer100g":10,"sugarPer100g":6}""",
        ["cheese_a"] = """{"category":"сир","price":80,"proteinPer100g":25,"sugarPer100g":1,"weightGrams":200}""",
        ["cheese_b"] = """{"category":"сир","price":75,"proteinPer100g":22,"sugarPer100g":1,"weightGrams":200}""",
        ["fish_a"] = """{"category":"риба","price":120,"proteinPer100g":20,"sugarPer100g":0,"weightGrams":300}""",
        ["eggs_a"] = """{"category":"яйця","price":45,"proteinPer100g":13,"sugarPer100g":0.5,"weightGrams":600}""",
    };
}

/// <summary>20-SKU fixture for gate 3.4 (15 complete + 5 "gap" products), split across two
/// weeks of orders so the median-weekly-receipt calculation has something to chew on.</summary>
public static class CoverageFixture
{
    public const string OfflineOrdersJson = """
        [
          {"totalAmount": 850.50, "createdAt": "2026-08-03T10:00:00Z",
           "items": [
             {"productId":"sku1"},{"productId":"sku2"},{"productId":"sku3"},
             {"productId":"sku4"},{"productId":"sku5"},{"productId":"gap1"},{"productId":"gap2"}
           ]},
          {"totalAmount": 920.00, "createdAt": "2026-08-10T10:00:00Z",
           "items": [
             {"productId":"sku6"},{"productId":"sku7"},{"productId":"sku8"},
             {"productId":"sku9"},{"productId":"sku10"},{"productId":"gap3"}
           ]}
        ]
        """;

    public const string OnlineOrdersJson = """
        [
          {"totalAmount": 430.25, "createdAt": "2026-08-10T18:00:00Z",
           "items": [
             {"productId":"sku11"},{"productId":"sku12"},{"productId":"sku13"},
             {"productId":"sku14"},{"productId":"sku15"},{"productId":"gap4"},{"productId":"gap5"}
           ]}
        ]
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
