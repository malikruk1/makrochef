using System.Text.Json;
using MakroChef.Domain.Cart;
using MakroChef.Mcp;

namespace MakroChef.Agent.Catalog;

/// <summary>TASKS.md 6.1 real-shape discovery (2026-09-14, `dotnet run -- diag`): silpo_get_products'
/// "category" filter needs a real category SLUG (e.g. "syr-kyslomolochnyi-4988"), not a guessed
/// free-text word like "сир" — passing the guessed word returned "No products found" every time,
/// which is why CandidatePoolBuilder's deficit-category search silently produced zero candidates
/// on a live account despite otherwise-correct nutrient parsing. Real category titles are also
/// far more granular than a flat "сир"/"риба" list (e.g. "Сир кисломолочний", "Сири м'які", "Сири
/// плавлені" are all separate categories) — this resolves a broad keyword to every matching
/// category's slug via a title substring match against silpo_get_categories.</summary>
public class CategoryResolver(IMakroChefMcpClient mcpClient, SessionContext session)
{
    private const int MaxSlugsPerKeyword = 3;

    private IReadOnlyList<(string Title, string Slug)>? _categories;

    public async Task<IReadOnlyList<string>> ResolveSlugsAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var categories = await EnsureCategoriesAsync(cancellationToken);
        return categories
            .Where(c => c.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Slug)
            .Take(MaxSlugsPerKeyword)
            .ToList();
    }

    private async Task<IReadOnlyList<(string Title, string Slug)>> EnsureCategoriesAsync(CancellationToken cancellationToken)
    {
        if (_categories is not null)
        {
            return _categories;
        }

        var json = await mcpClient.CallToolAsync(
            "silpo_get_categories",
            new Dictionary<string, object?>
            {
                ["branchId"] = session.BranchId,
                ["deliveryType"] = session.DeliveryType,
                ["timeslotStart"] = session.TimeslotStart,
                ["timeslotEnd"] = session.TimeslotEnd,
            },
            cancellationToken);

        _categories = ParseCategories(json);
        return _categories;
    }

    private static List<(string Title, string Slug)> ParseCategories(string json)
    {
        var result = new List<(string, string)>();
        try
        {
            var root = JsonDocument.Parse(json).RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var category in categories.EnumerateArray())
            {
                if (category.ValueKind == JsonValueKind.Object
                    && category.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                    && category.TryGetProperty("slug", out var slug) && slug.ValueKind == JsonValueKind.String)
                {
                    result.Add((title.GetString()!, slug.GetString()!));
                }
            }
        }
        catch (JsonException)
        {
            // Same tolerance as JsonFieldScanner.Parse - a bad response just yields no matches.
        }

        return result;
    }
}
