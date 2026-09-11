using System.Text.Json;
using MakroChef.Domain.Nutrition;

namespace MakroChef.Nutrition;

/// <summary>Fallback for path B (TASKS.md 4.0 / 3.4 STOP-2): raw `nutriments` by barcode, never
/// Nutri-Score — it's frequently uncomputed for Ukrainian products. Coded defensively: a miss,
/// a timeout, or a malformed response all just mean "no data", never an exception the caller
/// has to know about.</summary>
public class OpenFoodFactsClient(HttpClient httpClient)
{
    public OpenFoodFactsClient() : this(CreateDefaultHttpClient())
    {
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MakroChef/1.0 (hackathon project; contact via BLOCKERS.md)");
        return client;
    }

    public async Task<NutrientInfo?> FetchAsync(string barcode, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.GetAsync(
                $"https://world.openfoodfacts.org/api/v0/product/{Uri.EscapeDataString(barcode)}.json",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var root = JsonDocument.Parse(json).RootElement;

            if (!root.TryGetProperty("status", out var status) || status.GetInt32() != 1)
            {
                return null; // product not found
            }

            if (!root.TryGetProperty("product", out var product) || !product.TryGetProperty("nutriments", out var nutriments))
            {
                return null;
            }

            return new NutrientInfo(
                ReadNumber(nutriments, "proteins_100g"),
                ReadNumber(nutriments, "fat_100g"),
                ReadNumber(nutriments, "carbohydrates_100g"),
                ReadNumber(nutriments, "sugars_100g"),
                ReadNumber(nutriments, "energy-kcal_100g"),
                Source: "openfoodfacts");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    private static decimal? ReadNumber(JsonElement nutriments, string key) =>
        nutriments.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : null;
}
