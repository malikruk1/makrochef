using MakroChef.Agent.Catalog;
using Xunit;

namespace MakroChef.Tests.Catalog;

/// <summary>Regression test pinned to an actual silpo_get_product_details response captured
/// 2026-09-14 against a live account (BLOCKERS.md B-2).</summary>
public class ProductDetailsParserLiveFixtureTests
{
    private const string LivePizzaJson = """
        {"success":true,"product":{"id":"1edafa8e-3cdd-6ae0-a73c-df42cf4d0987","name":"Піца Американа","slug":"pitsa-amerykana-747288","price":109,"displayPrice":109,"oldPrice":189,"stock":0,"available":false,"weighted":false,"step":1,"displayRatio":"500г","companyId":"1ec88c5d-a050-669c-8467-570a157f3e31","branchId":"00000000-0000-0000-0000-000000000000","externalProductId":747288,"ratio":"шт","attributes":{"Білки (г)":10,"Жири (г)":8.2,"Вуглеводи (г)":31.9}}}
        """;

    [Fact]
    public void Parse_LivePizzaFixture_UnwrapsProductAndReadsPriceInKopecks()
    {
        var details = ProductDetailsParser.Parse("1edafa8e-3cdd-6ae0-a73c-df42cf4d0987", LivePizzaJson);

        Assert.Equal(10_900, details.PriceKopecks); // 109 UAH -> 10900 kopecks
    }

    [Fact]
    public void Parse_LivePizzaFixture_ParsesWeightFromDisplayRatio()
    {
        var details = ProductDetailsParser.Parse("x", LivePizzaJson);

        Assert.Equal(500m, details.WeightGrams); // "500г" -> 500g, not the 100g default
    }

    [Fact]
    public void Parse_LivePizzaFixture_OldPriceMeansOnPromotion()
    {
        var details = ProductDetailsParser.Parse("x", LivePizzaJson);

        Assert.True(details.OnPromotion); // oldPrice (189) > price (109) present
    }

    [Fact]
    public void Parse_LivePizzaFixture_NoCategory_FallsBackToUnknown()
    {
        var details = ProductDetailsParser.Parse("x", LivePizzaJson);

        Assert.Equal("невідома", details.Category); // real payload has no category field at all
    }

    [Theory]
    [InlineData("10 шт", 100)] // count-based unit - not a gram weight, falls back to default
    [InlineData("900г", 900)]
    public void Parse_DifferentDisplayRatios_ParsesOrFallsBackCorrectly(string displayRatio, decimal expectedWeight)
    {
        var json = "{\"success\":true,\"product\":{\"price\":10,\"displayRatio\":\"" + displayRatio + "\"}}";
        var details = ProductDetailsParser.Parse("x", json);

        Assert.Equal(expectedWeight, details.WeightGrams);
    }
}
