using MakroChef.Agent.Tracing;
using MakroChef.Data;
using MakroChef.Domain.Solver;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests;

public class LoggingBasketSolverTests
{
    [Fact]
    public async Task SolveAsync_RecordsOneMcpCallRow_WithToolNameSolverSolve()
    {
        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        var sessionId = Guid.NewGuid();
        var solver = new LoggingBasketSolver(new MakroChef.Solver.BasketSolver(), recorder, sessionId);

        var request = new SolverRequest(
            Candidates: [new Candidate("p1", "dairy", 1000, 20_000, 0, 100, false)],
            TargetProteinMg: 10_000,
            MaxSugarMg: 50_000,
            KcalMin: 10,
            KcalMax: 1000,
            BaselineCostKopecks: 10_000);

        await solver.SolveAsync(request);

        var calls = await db.McpCalls.ToListAsync();
        Assert.Single(calls);
        Assert.Equal("solver.solve", calls[0].Tool);
        Assert.Equal("success", calls[0].Status);
        Assert.Equal(sessionId, calls[0].SessionId);
    }

    [Fact]
    public async Task SolveAsync_TwoCallsSameSession_BothRecorded_ForReoptimizationTrace()
    {
        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        var sessionId = Guid.NewGuid();
        var solver = new LoggingBasketSolver(new MakroChef.Solver.BasketSolver(), recorder, sessionId);

        var request = new SolverRequest(
            Candidates: [new Candidate("p1", "dairy", 1000, 20_000, 0, 100, false)],
            TargetProteinMg: 10_000,
            MaxSugarMg: 50_000,
            KcalMin: 10,
            KcalMax: 1000,
            BaselineCostKopecks: 10_000);

        await solver.SolveAsync(request);
        await solver.SolveAsync(request); // simulates the 7.2 re-solve after an out-of-stock item

        var solverCalls = await db.McpCalls.Where(c => c.Tool == "solver.solve").ToListAsync();
        Assert.Equal(2, solverCalls.Count);
    }
}
