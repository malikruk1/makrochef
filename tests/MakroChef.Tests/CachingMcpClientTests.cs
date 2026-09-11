using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests;

public class CachingMcpClientTests
{
    [Fact]
    public async Task CallToolAsync_TwoConsecutiveCallsSameProduct_HitsNetworkExactlyOnce()
    {
        StubTools.GetProductDetailsCallCount = 0;

        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var recorder = new EfMcpCallRecorder(db);
        var inner = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);
        await using var client = new CachingMcpClient(inner, new CachingMcpClientOptions());

        var args = new Dictionary<string, object?> { ["productId"] = "sku-123" };
        var first = await client.CallToolAsync("get_product_details", args);
        var second = await client.CallToolAsync("get_product_details", args);

        Assert.Equal(first, second);
        Assert.Equal(1, StubTools.GetProductDetailsCallCount);
    }

    [Fact]
    public async Task CallToolAsync_DifferentProducts_EachHitsNetwork()
    {
        StubTools.GetProductDetailsCallCount = 0;

        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var recorder = new EfMcpCallRecorder(db);
        var inner = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);
        await using var client = new CachingMcpClient(inner, new CachingMcpClientOptions());

        await client.CallToolAsync("get_product_details", new Dictionary<string, object?> { ["productId"] = "sku-1" });
        await client.CallToolAsync("get_product_details", new Dictionary<string, object?> { ["productId"] = "sku-2" });

        Assert.Equal(2, StubTools.GetProductDetailsCallCount);
    }

    [Fact]
    public async Task CallToolAsync_UncachedTool_AlwaysHitsNetwork()
    {
        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var recorder = new EfMcpCallRecorder(db);
        var inner = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);
        await using var client = new CachingMcpClient(inner, new CachingMcpClientOptions());

        await client.CallToolAsync("silpo_ping", new Dictionary<string, object?> { ["message"] = "hi" });
        await client.CallToolAsync("silpo_ping", new Dictionary<string, object?> { ["message"] = "hi" });

        var loggedCalls = await db.McpCalls.Where(c => c.Tool == "silpo_ping").ToListAsync();
        Assert.Equal(2, loggedCalls.Count);
    }
}
