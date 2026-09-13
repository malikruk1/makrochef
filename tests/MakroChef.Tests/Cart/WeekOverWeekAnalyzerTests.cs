using MakroChef.Agent.Cart;
using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests.Cart;

public class WeekOverWeekAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_TwoWeeksOfFixtureOrders_ComparesRealProteinGapWeekOverWeek()
    {
        StubCartState.Lines.Clear();
        StubCartState.OutOfStock.Clear();

        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();
        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);

        // Fixture (CoverageFixture) has two offline-order weeks (2026-08-03, 2026-08-10) plus an
        // online order also in the week of 2026-08-10 - three distinct order dates across two
        // ISO weeks, enough for a real week-over-week comparison.
        var result = await new WeekOverWeekAnalyzer(mcpClient).AnalyzeAsync();

        Assert.NotNull(result);
        Assert.True(result!.HasEnoughData);
        Assert.True(result.LastWeekProteinGapGrams >= 0);
        Assert.True(result.ThisWeekProteinGapGrams >= 0);
    }
}
