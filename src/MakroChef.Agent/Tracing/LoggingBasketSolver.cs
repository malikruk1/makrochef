using System.Diagnostics;
using MakroChef.Domain.Mcp;
using MakroChef.Domain.Solver;

namespace MakroChef.Agent.Tracing;

/// <summary>Wraps BasketSolver so every solve — including the reoptimization re-solve in 7.2 —
/// shows up in the same McpCalls trace as the MCP calls around it (TASKS.md 8.3: the repeated
/// "solver.solve()" row is one of the two the whole demo pitch hangs on).</summary>
public class LoggingBasketSolver(MakroChef.Solver.BasketSolver inner, IMcpCallRecorder callRecorder, Guid? sessionId = null)
{
    public async Task<SolverResult> SolveAsync(SolverRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = inner.Solve(request);
        stopwatch.Stop();

        await callRecorder.RecordAsync(new McpCallRecord(
            Tool: "solver.solve",
            ArgsHash: $"candidates={request.Candidates.Count}",
            Status: result.Success ? "success" : "infeasible",
            DurationMs: (int)stopwatch.ElapsedMilliseconds,
            SessionId: sessionId), cancellationToken);

        return result;
    }
}
