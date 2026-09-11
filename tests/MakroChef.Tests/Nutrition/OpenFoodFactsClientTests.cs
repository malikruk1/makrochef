using System.Net;
using MakroChef.Nutrition;
using MakroChef.Tests.OAuth;
using Xunit;

namespace MakroChef.Tests.Nutrition;

public class OpenFoodFactsClientTests
{
    [Fact]
    public async Task FetchAsync_KnownBarcode_ReturnsRawNutriments()
    {
        const string json = """
        {
          "status": 1,
          "product": {
            "nutriments": {
              "proteins_100g": 3.5,
              "fat_100g": 1.2,
              "carbohydrates_100g": 5.0,
              "sugars_100g": 4.1,
              "energy-kcal_100g": 60
            }
          }
        }
        """;
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        var client = new OpenFoodFactsClient(new HttpClient(handler));

        var result = await client.FetchAsync("4820000000000");

        Assert.NotNull(result);
        Assert.Equal("openfoodfacts", result!.Source);
        Assert.Equal(3.5m, result.ProteinPer100g);
        Assert.Equal(4.1m, result.SugarPer100g);
    }

    [Fact]
    public async Task FetchAsync_ProductNotFound_ReturnsNull()
    {
        const string json = """{"status": 0}""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        var client = new OpenFoodFactsClient(new HttpClient(handler));

        var result = await client.FetchAsync("0000000000000");

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchAsync_ServerError_ReturnsNullInsteadOfThrowing()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new OpenFoodFactsClient(new HttpClient(handler));

        var result = await client.FetchAsync("4820000000000");

        Assert.Null(result);
    }

    [Fact]
    public async Task FetchAsync_MalformedJson_ReturnsNullInsteadOfThrowing()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json"),
        });
        var client = new OpenFoodFactsClient(new HttpClient(handler));

        var result = await client.FetchAsync("4820000000000");

        Assert.Null(result);
    }
}
