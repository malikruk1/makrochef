using MakroChef.Domain.Entities;
using MakroChef.Domain.Mcp;

namespace MakroChef.Data;

public class EfMcpCallRecorder(MakroChefDbContext db) : IMcpCallRecorder
{
    public async Task RecordAsync(McpCallRecord record, CancellationToken cancellationToken = default)
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
}
