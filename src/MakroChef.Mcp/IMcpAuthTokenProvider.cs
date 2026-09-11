namespace MakroChef.Mcp;

/// <summary>Supplies the current bearer token for outgoing MCP requests. The OAuth 2.1 + PKCE
/// flow that produces this token is implemented separately (TASKS.md 3.2); this project only
/// needs to be told what to send.</summary>
public interface IMcpAuthTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Used where no auth is configured yet (local dev, stub-server tests).</summary>
public class NullMcpAuthTokenProvider : IMcpAuthTokenProvider
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
