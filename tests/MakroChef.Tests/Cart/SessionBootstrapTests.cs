using MakroChef.Agent.Cart;
using MakroChef.Data;
using MakroChef.Mcp;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests.Cart;

/// <summary>Gate 3.3: silpo_get_my_shopping_cart -> silpo_get_shopping_cart_by_id yields
/// branchId/deliveryType/timeslot for the rest of the session.</summary>
public class SessionBootstrapTests
{
    [Fact]
    public async Task EnsureAsync_ExistingCart_ReturnsBranchDeliveryAndTimeslot()
    {
        StubCartState.Lines.Clear();
        StubCartState.OutOfStock.Clear();

        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();
        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var client = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);

        var context = await new SessionBootstrap(client).EnsureAsync();

        Assert.NotNull(context);
        Assert.Equal("stub-cart-1", context!.ShoppingCartId);
        Assert.Equal("stub-branch-1", context.BranchId);
        Assert.Equal("SelfPickup", context.DeliveryType);
        Assert.Equal("2026-09-14T06:00:00+00:00", context.TimeslotStart);
        Assert.Equal("2026-09-14T06:30:00+00:00", context.TimeslotEnd);
    }
}
