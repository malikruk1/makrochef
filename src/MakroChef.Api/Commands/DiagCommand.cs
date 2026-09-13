using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Mcp.OAuth;

namespace MakroChef.Api.Commands;

/// <summary>Temporary: `dotnet run -- diag` prints raw MCP tool responses so we can see the
/// real payload shape against a live account, instead of guessing from tools/list input
/// schemas alone. Remove once the real schema is confirmed and parsers are adjusted.</summary>
public static class DiagCommand
{
    public static async Task<int> RunAsync(Uri mcpBaseUri, Guid userId, MakroChefDbContext db, EfMcpTokenStore tokenStore, TokenEncryptor tokenEncryptor)
    {
        var stored = await tokenStore.FindByUserAsync(userId);
        if (stored is null)
        {
            Console.Error.WriteLine("Немає токена.");
            return 1;
        }

        var accessToken = tokenEncryptor.Decrypt(new EncryptedToken(stored.EncryptedAccessToken, stored.AccessTokenNonce));
        var tokenProvider = new StaticTokenProvider(accessToken);
        var recorder = new EfMcpCallRecorder(db);
        await using var client = new MakroChefMcpClient(mcpBaseUri, tokenProvider, recorder, userId);

        Console.WriteLine("=== tools/list ===");
        var tools = await client.ListToolsAsync();
        foreach (var tool in tools)
        {
            Console.WriteLine($"- {tool.Name}: {tool.Description}");
        }

        Console.WriteLine();
        Console.WriteLine("=== get_my_offline_orders (raw) ===");
        try
        {
            var offline = await client.CallToolAsync("silpo_get_my_offline_orders", new Dictionary<string, object?>());
            Console.WriteLine(offline);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("=== get_my_online_orders (raw) ===");
        try
        {
            var online = await client.CallToolAsync("silpo_get_my_online_orders", new Dictionary<string, object?>());
            Console.WriteLine(online);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }

        return 0;
    }

    private class StaticTokenProvider(string token) : IMcpAuthTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(token);

        public Task<string?> ForceRefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
