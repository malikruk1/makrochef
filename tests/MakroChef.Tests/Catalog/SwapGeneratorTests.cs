using MakroChef.Agent.Catalog;
using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Nutrition;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests.Catalog;

/// <summary>Gate 6.2: at least 5 swap pairs with a computed protein/sugar/price delta.</summary>
public class SwapGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_UsualCart_ProducesAtLeastFiveSwapsWithDeltas()
    {
        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);
        var resolver = new ExactMcpNutritionResolver(mcpClient);

        var usualCart = new[] { "yogurt_x", "bread_x", "milk_x", "juice_x", "cereal_x" };

        var swaps = await new SwapGenerator(mcpClient, resolver).GenerateAsync(usualCart);

        Assert.True(swaps.Count >= 5, $"Expected >=5 swaps, got {swaps.Count}");

        var yogurtSwap = swaps.Single(s => s.OldProductId == "yogurt_x" && s.NewProductId == "yogurt_y");
        Assert.Equal(2m, yogurtSwap.ProteinDeltaGrams);
        Assert.Equal(-4m, yogurtSwap.SugarDeltaGrams);
        Assert.Equal(-300, yogurtSwap.PriceDeltaKopecks);

        // yogurt_z is worse on both protein and sugar - must never show up as a swap.
        Assert.DoesNotContain(swaps, s => s.NewProductId == "yogurt_z");
    }
}
