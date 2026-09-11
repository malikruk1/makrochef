using System.Net;
using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Nutrition;
using MakroChef.Tests.OAuth;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests.Nutrition;

public class CategoryIndexNutritionResolverTests
{
    [Fact]
    public async Task ResolveAsync_McpDataComplete_DoesNotCallOpenFoodFacts()
    {
        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);

        var offCalled = false;
        var offHandler = new FakeHttpMessageHandler(_ => { offCalled = true; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }; });
        var resolver = new CategoryIndexNutritionResolver(new ExactMcpNutritionResolver(mcpClient), new OpenFoodFactsClient(new HttpClient(offHandler)));

        var result = await resolver.ResolveAsync("sku1", barcode: "4820000000000");

        Assert.NotNull(result);
        Assert.Equal("mcp", result!.Source);
        Assert.False(offCalled, "Complete MCP data shouldn't need the Open Food Facts fallback.");
    }

    [Fact]
    public async Task ResolveAsync_McpDataIncomplete_FallsBackToOpenFoodFacts()
    {
        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);

        const string offJson = """{"status":1,"product":{"nutriments":{"proteins_100g":3.5,"sugars_100g":4.1}}}""";
        var offHandler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(offJson) });
        var resolver = new CategoryIndexNutritionResolver(new ExactMcpNutritionResolver(mcpClient), new OpenFoodFactsClient(new HttpClient(offHandler)));

        var result = await resolver.ResolveAsync("gap1", barcode: "4820000000000");

        Assert.NotNull(result);
        Assert.Equal("openfoodfacts", result!.Source);
        Assert.Equal(3.5m, result.ProteinPer100g);
    }

    [Fact]
    public async Task ResolveAsync_McpDataIncompleteAndNoBarcode_ReturnsPartialMcpData()
    {
        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);

        var offCalled = false;
        var offHandler = new FakeHttpMessageHandler(_ => { offCalled = true; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }; });
        var resolver = new CategoryIndexNutritionResolver(new ExactMcpNutritionResolver(mcpClient), new OpenFoodFactsClient(new HttpClient(offHandler)));

        var result = await resolver.ResolveAsync("gap1", barcode: null);

        Assert.NotNull(result);
        Assert.Equal("mcp", result!.Source);
        Assert.Null(result.SugarPer100g);
        Assert.False(offCalled);
    }
}
