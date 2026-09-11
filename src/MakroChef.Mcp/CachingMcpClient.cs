using MakroChef.Domain.Mcp;
using Microsoft.Extensions.Caching.Memory;

namespace MakroChef.Mcp;

/// <summary>Decorates an IMakroChefMcpClient with a per-tool TTL cache so repeated lookups of
/// the same product/category don't burn through rate limits (TASKS.md 3.3: "без кешу впрешся
/// в 429"). Concurrent calls for the same key share one in-flight request instead of firing
/// duplicate network calls.</summary>
public class CachingMcpClient(IMakroChefMcpClient inner, CachingMcpClientOptions options) : IMakroChefMcpClient
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default) =>
        inner.ListToolsAsync(cancellationToken);

    public Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        var ttl = options.GetTtl(toolName);
        if (ttl is null)
        {
            return inner.CallToolAsync(toolName, arguments, cancellationToken);
        }

        var key = BuildCacheKey(toolName, arguments);

        // Lazy<Task<T>> so two callers racing for the same missing key share one in-flight
        // call instead of both hitting the network (the Lazy, not just its result, is cached).
        var lazyResult = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl.Value;
            return new Lazy<Task<string>>(() => inner.CallToolAsync(toolName, arguments, cancellationToken));
        })!;

        return lazyResult.Value;
    }

    private static string BuildCacheKey(string toolName, IReadOnlyDictionary<string, object?> arguments)
    {
        var sortedArgs = string.Join(",", arguments.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{toolName}|{sortedArgs}";
    }

    public async ValueTask DisposeAsync()
    {
        _cache.Dispose();
        await inner.DisposeAsync();
    }
}
