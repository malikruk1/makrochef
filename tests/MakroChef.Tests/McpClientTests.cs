using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests;

public class McpClientTests
{
    [Fact]
    public async Task ListToolsAsync_ReadsFromStubServer_AndRecordsCallInMcpCalls()
    {
        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();

        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        var recorder = new EfMcpCallRecorder(db);
        await using var client = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);

        var tools = await client.ListToolsAsync();

        Assert.Contains(tools, t => t.Name == "silpo_ping");

        var loggedCalls = await db.McpCalls.ToListAsync();
        Assert.Single(loggedCalls);
        Assert.Equal("tools/list", loggedCalls[0].Tool);
        Assert.Equal("success", loggedCalls[0].Status);
    }
}
