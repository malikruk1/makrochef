using MakroChef.Agent.Coverage;
using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests;

public class CoverageProbeTests
{
    [Fact]
    public async Task RunAsync_TwentyProductFixture_ProducesCorrectReport()
    {
        StubTools.GetProductDetailsCallCount = 0;

        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var client = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);

        var report = await new CoverageProbe(client, StubSession.Default).RunAsync();

        Assert.Equal(20, report.UniqueSkuCount);
        Assert.Equal(15, report.FullMacroCount);
        Assert.Equal(75, report.CoveragePercent, precision: 0);

        Assert.True(report.CountByCategory.ContainsKey("молочні"));
        Assert.Equal(15, report.CountByCategory["молочні"]);
        Assert.True(report.CountByCategory.ContainsKey("ваговий"));
        Assert.Equal(5, report.CountByCategory["ваговий"]);

        // "ваговий" is 0% complete (all 5 gap-products lack sugar) -> flagged as a gap.
        Assert.Contains(report.Gaps, g => g.StartsWith("ваговий"));
        // "власне виробництво" and "фреш" never appear at all in the fixture -> flagged too.
        Assert.Contains(report.Gaps, g => g.StartsWith("власне виробництво"));
        Assert.Contains(report.Gaps, g => g.StartsWith("фреш"));

        // Two weeks of orders: week of 2026-08-03 (850.50) and week of 2026-08-10 (920.00 + 430.25 = 1350.25).
        Assert.NotNull(report.MedianWeeklyReceipt);
        Assert.Equal((850.50m + 1350.25m) / 2, report.MedianWeeklyReceipt!.Value);

        var markdown = report.ToMarkdown();
        Assert.Contains("Покриття: 75%", markdown);
        Assert.Contains("Унікальних SKU: 20", markdown);
        Assert.Contains("З повним Б/Ж/В/цукром: 15", markdown);
    }
}
