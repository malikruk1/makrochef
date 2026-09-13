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

    /// <summary>Confirmed live (2026-09-14, `dotnet run -- diag` against a real product): the
    /// energy attribute is a string like "502,3/2101,6" using Ukrainian comma-decimal formatting,
    /// not a dot as originally guessed ("241/1013"). Parsing "502,3" as InvariantCulture without
    /// normalizing the comma first can silently produce 5023 instead of 502.3 (NumberStyles.Any
    /// treats the comma as a thousands separator) - this pins the fix.</summary>
    [Fact]
    public void ParseNutrients_CommaDecimalEnergyLabel_ParsesCorrectly_NotTenXTooLarge()
    {
        var json = """{"success":true,"product":{"attributes":{"Енергетична цінність (кКал/кДЖ)":"502,3/2101,6","Білки (г)":24.7}}}""";

        var (_, _, _, _, kcal) = ExactMcpNutritionResolver.ParseNutrients(json);

        Assert.Equal(502.3m, kcal);
    }

    /// <summary>Same comma-decimal risk applies to any string-typed macro attribute (weighed
    /// goods sometimes report them as strings rather than JSON numbers).</summary>
    [Fact]
    public void ParseNutrients_CommaDecimalStringAttribute_ParsesCorrectly()
    {
        var json = """{"success":true,"product":{"attributes":{"Білки (г)":"24,7"}}}""";

        var (protein, _, _, _, _) = ExactMcpNutritionResolver.ParseNutrients(json);

        Assert.Equal(24.7m, protein);
    }
}
