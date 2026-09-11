using MakroChef.Data;
using Microsoft.EntityFrameworkCore;

namespace MakroChef.Api;

public record TraceCallDto(string Tool, string ArgsHash, string Status, int DurationMs, DateTimeOffset CreatedAt);

public static class TraceQuery
{
    public static async Task<List<TraceCallDto>> GetCallsAsync(MakroChefDbContext db, Guid sessionId, CancellationToken cancellationToken = default) =>
        await db.McpCalls
            .Where(c => c.SessionId == sessionId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new TraceCallDto(c.Tool, c.ArgsHash, c.Status, c.DurationMs, c.CreatedAt))
            .ToListAsync(cancellationToken);
}
