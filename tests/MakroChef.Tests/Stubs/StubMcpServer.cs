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

    [McpServerTool(Name = "get_product_details"), Description("Stub tool for gate 3.3 cache test.")]
    public static string GetProductDetails(string productId)
    {
        Interlocked.Increment(ref GetProductDetailsCallCount);
        return $"{{\"productId\":\"{productId}\",\"proteinPer100g\":10}}";
    }
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
