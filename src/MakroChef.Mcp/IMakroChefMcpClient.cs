using MakroChef.Domain.Mcp;

namespace MakroChef.Mcp;

/// <summary>Thin domain-facing MCP contract. Nothing outside this project should know or care
/// whether it's backed by the official SDK or a hand-rolled HTTP client (TASKS.md 3.1) — that
/// decision lives entirely behind this interface.</summary>
public interface IMakroChefMcpClient : IAsyncDisposable
{
    Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default);

    Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default);
}
