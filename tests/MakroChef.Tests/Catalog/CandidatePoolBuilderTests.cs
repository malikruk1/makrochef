using MakroChef.Agent.Catalog;
using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Nutrition;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests.Catalog;

/// <summary>Gate 6.1 (pipeline correctness — pool size of 200-400 needs a live catalog,
/// BLOCKERS.md B-2): parsing, price-after-discount, per-100g-to-per-unit nutrient conversion,
/// and hard category restriction all verified against the stub fixture.</summary>
public class CandidatePoolBuilderTests
{
    [Fact]
    public async Task BuildAsync_SeedAndDeficitCategories_ProducesCandidatesWithConvertedNutrients()
    {
        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);
        var resolver = new ExactMcpNutritionResolver(mcpClient, StubSession.Default);

        var request = new CandidatePoolRequest(
            SeedSlugsById: new Dictionary<string, string> { ["yogurt_x"] = "yogurt_x", ["cheese_a"] = "cheese_a" },
            DeficitCategories: ["сир", "риба", "яйця"],
            RestrictedCategories: []);

        var candidates = await new CandidatePoolBuilder(mcpClient, resolver, StubSession.Default).BuildAsync(request);

        Assert.Contains(candidates, c => c.ProductId == "cheese_a");
        Assert.Contains(candidates, c => c.ProductId == "fish_a");
        Assert.Contains(candidates, c => c.ProductId == "eggs_a");

        // cheese_a: 25g protein/100g, weightGrams=200 -> 50g = 50000mg per unit.
        var cheeseA = candidates.Single(c => c.ProductId == "cheese_a");
        Assert.Equal(50_000, cheeseA.ProteinMg);
        Assert.Equal(8_000, cheeseA.PriceKopecks);
        Assert.False(cheeseA.Restricted);
    }

    [Fact]
    public async Task BuildAsync_RestrictedCategory_MarksMatchingCandidatesRestricted()
    {
        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);
        var resolver = new ExactMcpNutritionResolver(mcpClient, StubSession.Default);

        var request = new CandidatePoolRequest(
            SeedSlugsById: new Dictionary<string, string>(),
            DeficitCategories: ["риба"],
            RestrictedCategories: ["риба"]);

        var candidates = await new CandidatePoolBuilder(mcpClient, resolver, StubSession.Default).BuildAsync(request);

        var fish = candidates.Single(c => c.ProductId == "fish_a");
        Assert.True(fish.Restricted);
    }
}
