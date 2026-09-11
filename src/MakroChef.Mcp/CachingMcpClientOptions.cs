namespace MakroChef.Mcp;

/// <summary>Per-tool cache TTLs. TASKS.md 3.3: nutrients cache forever, prices 15 minutes —
/// without a live account (BLOCKERS.md B-2) we can't yet see the real get_product_details
/// payload shape to split nutrient fields from price fields, so both are cached under one TTL
/// for now. Tracked as tech debt in CHECKPOINTS.md: revisit once a real response is available.</summary>
public class CachingMcpClientOptions
{
    public static readonly TimeSpan Forever = TimeSpan.FromDays(365);

    private readonly Dictionary<string, TimeSpan> _ttlByTool = new()
    {
        ["get_product_details"] = TimeSpan.FromMinutes(15),
        ["get_categories_tree"] = Forever,
    };

    public TimeSpan? GetTtl(string toolName) => _ttlByTool.TryGetValue(toolName, out var ttl) ? ttl : null;

    public void SetTtl(string toolName, TimeSpan ttl) => _ttlByTool[toolName] = ttl;
}
