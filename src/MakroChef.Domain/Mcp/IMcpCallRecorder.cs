namespace MakroChef.Domain.Mcp;

/// <summary>Persists an audit trail of MCP tool calls (source data for the trace panel,
/// TASKS.md 8.3). Lives in Domain so MakroChef.Mcp never references the data layer directly —
/// the MCP layer stays pure I/O.</summary>
public interface IMcpCallRecorder
{
    Task RecordAsync(McpCallRecord record, CancellationToken cancellationToken = default);
}

public record McpCallRecord(string Tool, string ArgsHash, string Status, int DurationMs, Guid? SessionId = null);
