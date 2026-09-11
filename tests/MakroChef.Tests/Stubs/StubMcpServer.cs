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

    [McpServerTool(Name = "get_product_details"), Description("Stub tool for gate 3.3/3.4 tests.")]
    public static string GetProductDetails(string productId)
    {
        Interlocked.Increment(ref GetProductDetailsCallCount);

        // Deterministic fixture for gate 3.4: "gapN" products simulate a category with
        // systematically incomplete nutrient data (e.g. weighed goods), everything else
        // has full Б/Ж/В/цукор.
        if (productId.StartsWith("gap", StringComparison.Ordinal))
        {
            return $$"""{"productId":"{{productId}}","category":"ваговий","proteinPer100g":8}""";
        }

        return $$"""
            {"productId":"{{productId}}","category":"молочні","proteinPer100g":10,"fatPer100g":5,"carbsPer100g":12,"sugarPer100g":6}
            """;
    }

    [McpServerTool(Name = "get_my_offline_orders"), Description("Stub fixture for gate 3.4.")]
    public static string GetMyOfflineOrders() => CoverageFixture.OfflineOrdersJson;

    [McpServerTool(Name = "get_my_online_orders"), Description("Stub fixture for gate 3.4.")]
    public static string GetMyOnlineOrders() => CoverageFixture.OnlineOrdersJson;

    [McpServerTool(Name = "get_my_profile"), Description("Stub fixture for gate 4.1.")]
    public static string GetMyProfile() => """{"birthDate":"1995-06-15T00:00:00Z"}""";

    [McpServerTool(Name = "get_my_family"), Description("Stub fixture for gate 4.1.")]
    public static string GetMyFamily() => """{"members":[{"age":8},{"age":40}]}""";

    [McpServerTool(Name = "get_my_food_restrictions"), Description("Stub fixture for gate 4.1.")]
    public static string GetMyFoodRestrictions() => """{"restrictions":["риба","горіхи"]}""";

    [McpServerTool(Name = "get_my_delivery_addresses"), Description("Stub fixture for gate 4.1.")]
    public static string GetMyDeliveryAddresses() => """[{"city":"Київ","street":"Хрещатик"}]""";

    [McpServerTool(Name = "get_loyalty_info"), Description("Stub fixture for gate 4.1.")]
    public static string GetLoyaltyInfo() => """{"bonusBalance":275.5}""";
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
