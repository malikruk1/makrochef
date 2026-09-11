namespace MakroChef.Mcp;

/// <summary>Supplies the current bearer token for outgoing MCP requests. The OAuth 2.1 + PKCE
/// flow that produces this token is implemented separately (TASKS.md 3.2); this project only
/// needs to be told what to send.</summary>
public interface IMcpAuthTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Called on a hard 401 from the server (not just "close to expiry") — forces a
    /// refresh regardless of the cached token's apparent validity. Returns the new token, or
    /// null if refresh isn't possible (no refresh token, refresh itself failed).</summary>
    Task<string?> ForceRefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>Used where no auth is configured yet (local dev, stub-server tests).</summary>
public class NullMcpAuthTokenProvider : IMcpAuthTokenProvider
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> ForceRefreshAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
