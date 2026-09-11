using MakroChef.Agent.Profiling;
using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests.Profiling;

public class GuestContextCollectorTests
{
    [Fact]
    public async Task CollectAsync_StubFixture_ParsesAllFields()
    {
        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var client = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);

        var profile = await new GuestContextCollector(client).CollectAsync();

        var expectedAge = DateTimeOffset.UtcNow.Year - 1995 - (DateTimeOffset.UtcNow.Month < 6 || (DateTimeOffset.UtcNow.Month == 6 && DateTimeOffset.UtcNow.Day < 15) ? 1 : 0);
        Assert.Equal(expectedAge, profile.AgeYears);

        Assert.Equal(2, profile.Family.Count);
        Assert.Contains(profile.Family, f => f.IsChild && f.AgeYears == 8);
        Assert.Contains(profile.Family, f => !f.IsChild && f.AgeYears == 40);

        Assert.Equal(["риба", "горіхи"], profile.Restrictions);
        Assert.True(profile.HasSavedAddress);
        Assert.Equal(275.5m, profile.LoyaltyBonusBalance);
    }
}
