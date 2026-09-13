using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Nutrition;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests.Nutrition;

public class ExactMcpNutritionResolverTests
{
    [Fact]
    public async Task ResolveAsync_ProductWithFullMacros_ReturnsAllFields()
    {
        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var client = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);

        var resolver = new ExactMcpNutritionResolver(client, StubSession.Default);
        var result = await resolver.ResolveAsync("sku1", barcode: null);

        Assert.NotNull(result);
        Assert.Equal("mcp", result!.Source);
        Assert.Equal(10, result.ProteinPer100g);
        Assert.Equal(5, result.FatPer100g);
        Assert.Equal(12, result.CarbsPer100g);
        Assert.Equal(6, result.SugarPer100g);
    }

    [Fact]
    public async Task ResolveAsync_GapProduct_ReturnsPartialData_MissingFieldsNull()
    {
        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var client = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);

        var resolver = new ExactMcpNutritionResolver(client, StubSession.Default);
        var result = await resolver.ResolveAsync("gap1", barcode: null);

        Assert.NotNull(result);
        Assert.Equal(8, result!.ProteinPer100g);
        Assert.Null(result.SugarPer100g);
    }
}
