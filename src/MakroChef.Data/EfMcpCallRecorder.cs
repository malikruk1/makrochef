using MakroChef.Domain.Entities;
using MakroChef.Domain.Mcp;

namespace MakroChef.Data;

public class EfMcpCallRecorder(MakroChefDbContext db) : IMcpCallRecorder
{
    // EF Core's DbContext is not thread-safe - callers now resolve candidates concurrently
    // (CandidatePoolBuilder), so every write through this one scoped DbContext must be serialized,
    // even though the MCP HTTP calls themselves run in parallel just fine.
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public async Task RecordAsync(McpCallRecord record, CancellationToken cancellationToken = default)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            db.McpCalls.Add(new McpCall
            {
                Id = Guid.NewGuid(),
                SessionId = record.SessionId,
                Tool = record.Tool,
                ArgsHash = record.ArgsHash,
                Status = record.Status,
                DurationMs = record.DurationMs,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            Lock.Release();
        }
    }
}
