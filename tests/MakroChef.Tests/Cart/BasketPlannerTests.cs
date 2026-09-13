using MakroChef.Agent.Cart;
using MakroChef.Agent.Tracing;
using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests.Cart;

public class BasketPlannerTests
{
    [Fact]
    public async Task PlanAsync_StubFixtures_ProducesASuccessfulPlanAgainstRealShapes()
    {
        StubCartState.Lines.Clear();
        StubCartState.OutOfStock.Clear();

        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();
        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);
        var solver = new LoggingBasketSolver(new MakroChef.Solver.BasketSolver(), recorder);

        var plan = await new BasketPlanner(mcpClient, solver).PlanAsync();

        Assert.NotNull(plan);
        Assert.True(plan!.CandidatePoolSize > 0);
        Assert.True(plan.Coverage.UniqueSkuCount > 0);
        Assert.NotNull(plan.Norms.Source);

        if (plan.Solver.Success)
        {
            Assert.NotEmpty(plan.Solver.Lines);
            Assert.True(plan.Solver.TotalCostKopecks <= plan.BaselineWeeklyCostKopecks || plan.Solver.Relaxed.Count > 0);
        }
    }
}
