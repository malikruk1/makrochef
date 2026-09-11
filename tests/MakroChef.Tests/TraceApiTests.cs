using MakroChef.Api;
using MakroChef.Data;
using MakroChef.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests;

/// <summary>Gate 8.3: "на записаній сесії з БД панель показує ≥20 викликів у порядку
/// виконання; рядок повторного солвера візуально виділений." Tests the query the /api/trace
/// endpoint runs directly (WebApplicationFactory + swapping EF providers turned out to be
/// too fragile to be worth it here) — the visual highlighting itself is plain client-side JS
/// in web/trace/index.html, verified by eye in a browser, not unit-testable without one.</summary>
public class TraceApiTests
{
    [Fact]
    public async Task GetCallsAsync_SeededSessionWithTwentyTwoCalls_ReturnsAllInExecutionOrder()
    {
        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var sessionId = Guid.NewGuid();
        var baseTime = DateTimeOffset.UtcNow;

        for (var i = 0; i < 20; i++)
        {
            db.McpCalls.Add(new McpCall
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                Tool = $"get_products_{i}",
                ArgsHash = "-",
                Status = "success",
                DurationMs = 10,
                CreatedAt = baseTime.AddMilliseconds(i * 10),
            });
        }

        // The two rows the whole demo pitch hangs on (TASKS.md 8.3): a validation failure
        // followed by a second solver.solve() call proving the basket was reoptimized.
        db.McpCalls.Add(new McpCall
        {
            Id = Guid.NewGuid(), SessionId = sessionId, Tool = "solver.solve", ArgsHash = "-",
            Status = "success", DurationMs = 50, CreatedAt = baseTime.AddMilliseconds(200),
        });
        db.McpCalls.Add(new McpCall
        {
            Id = Guid.NewGuid(), SessionId = sessionId, Tool = "get_shopping_cart_by_id", ArgsHash = "-",
            Status = "1 out of stock", DurationMs = 15, CreatedAt = baseTime.AddMilliseconds(210),
        });
        db.McpCalls.Add(new McpCall
        {
            Id = Guid.NewGuid(), SessionId = sessionId, Tool = "solver.solve", ArgsHash = "-",
            Status = "success", DurationMs = 45, CreatedAt = baseTime.AddMilliseconds(220),
        });

        // A different session's calls must never leak into this session's trace.
        db.McpCalls.Add(new McpCall
        {
            Id = Guid.NewGuid(), SessionId = Guid.NewGuid(), Tool = "unrelated_session_call", ArgsHash = "-",
            Status = "success", DurationMs = 1, CreatedAt = baseTime,
        });

        await db.SaveChangesAsync();

        var calls = await TraceQuery.GetCallsAsync(db, sessionId);

        Assert.True(calls.Count >= 20, $"Expected >=20 calls, got {calls.Count}");
        Assert.DoesNotContain(calls, c => c.Tool == "unrelated_session_call");

        Assert.Equal(calls.OrderBy(c => c.CreatedAt).Select(c => c.Tool), calls.Select(c => c.Tool));

        var solverCalls = calls.Where(c => c.Tool == "solver.solve").ToList();
        Assert.Equal(2, solverCalls.Count);

        var outOfStockRow = calls.Single(c => c.Status.Contains("out of stock"));
        Assert.True(outOfStockRow.CreatedAt < solverCalls[1].CreatedAt, "Validation failure must precede the re-solve it triggered.");
    }
}
