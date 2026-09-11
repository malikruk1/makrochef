using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MakroChef.Domain.Mcp;
using MakroChef.Mcp.OAuth;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MakroChef.Mcp;

public class MakroChefMcpClient : IMakroChefMcpClient
{
    private readonly IMcpCallRecorder _callRecorder;
    private readonly Guid? _sessionId;
    private readonly Lazy<Task<McpClient>> _client;

    public MakroChefMcpClient(Uri endpoint, IMcpAuthTokenProvider tokenProvider, IMcpCallRecorder callRecorder, Guid? sessionId = null)
    {
        _callRecorder = callRecorder;
        _sessionId = sessionId;
        _client = new Lazy<Task<McpClient>>(() => CreateClientAsync(endpoint, tokenProvider, sessionId));
    }

    private static async Task<McpClient> CreateClientAsync(Uri endpoint, IMcpAuthTokenProvider tokenProvider, Guid? sessionId)
    {
        var httpClient = new HttpClient(new ResilientMcpHandler(tokenProvider, sessionId?.ToString()));
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = endpoint }, httpClient);
        return await McpClient.CreateAsync(transport);
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        return await RecordedCall("tools/list", args: null, async () =>
        {
            var client = await _client.Value;
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            return tools
                .Select(t => new McpToolDescriptor(t.Name, t.Description, JsonSerializer.Serialize(t.JsonSchema)))
                .ToList();
        });
    }

    public async Task<string> CallToolAsync(string toolName, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        return await RecordedCall(toolName, arguments, async () =>
        {
            var client = await _client.Value;
            var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
            return ExtractText(result.Content);
        });
    }

    private async Task<T> RecordedCall<T>(string tool, IReadOnlyDictionary<string, object?>? args, Func<Task<T>> call)
    {
        var stopwatch = Stopwatch.StartNew();
        var status = "success";
        try
        {
            return await call();
        }
        catch
        {
            status = "error";
            throw;
        }
        finally
        {
            stopwatch.Stop();
            await _callRecorder.RecordAsync(new McpCallRecord(
                Tool: tool,
                ArgsHash: HashArgs(args),
                Status: status,
                DurationMs: (int)stopwatch.ElapsedMilliseconds,
                SessionId: _sessionId));
        }
    }

    /// <summary>Tool results come back as a list of content blocks (text, image, ...); MCP tool
    /// responses we care about are JSON encoded as text blocks, so concatenate those and hand
    /// back plain text/JSON rather than the SDK's wrapper shape.</summary>
    private static string ExtractText(IList<ContentBlock> content) =>
        string.Concat(content.OfType<TextContentBlock>().Select(t => t.Text));

    /// <summary>Hash, never log raw args — they can carry addresses, phone numbers, etc.</summary>
    private static string HashArgs(IReadOnlyDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0)
        {
            return "-";
        }

        var canonical = JsonSerializer.Serialize(args.OrderBy(kv => kv.Key, StringComparer.Ordinal));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsValueCreated)
        {
            var client = await _client.Value;
            await client.DisposeAsync();
        }
    }
}
