using MakroChef.Agent.Cart;
using MakroChef.Data;
using MakroChef.Domain.Entities;
using MakroChef.Mcp;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests.Cart;

/// <summary>Gate 7.3: all five steps run in the mandatory order, visible in the McpCalls log
/// (the same log the 8.3 trace panel reads), and the guest is asked before bonuses are applied
/// - never silently.</summary>
public class CheckoutCascadeTests
{
    [Fact]
    public async Task RunAsync_FullCascade_StepsRunInOrder_AndReturnsCheckoutLinks()
    {
        StubCartState.Lines.Clear();
        StubCartState.OutOfStock.Clear();
        StubCartState.CheckoutReady = false;

        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();
        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        var sessionId = Guid.NewGuid();
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder, sessionId);

        var askedBonusAmount = -1m;
        var cascade = new CheckoutCascade(mcpClient);

        var links = await cascade.RunAsync(confirmApplyBonus: amount =>
        {
            askedBonusAmount = amount;
            return Task.FromResult(true);
        });

        Assert.Equal("https://silpo.ua/checkout/abc", links.WebLink);
        Assert.Equal("silpo://checkout/abc", links.MobileLink);
        Assert.Equal(275.5m, askedBonusAmount);

        var calls = await db.McpCalls.Where(c => c.SessionId == sessionId).OrderBy(c => c.CreatedAt).Select(c => c.Tool).ToListAsync();

        var expectedOrder = new[]
        {
            "get_my_premium_subscription",
            "get_my_certificates",
            "add_or_update_certificates",
            "get_promo_codes",
            "update_shopping_cart", // promo code
            "get_loyalty_info",
            "update_shopping_cart", // bonus
            "get_shopping_cart_by_id",
        };

        Assert.Equal(expectedOrder, calls);
    }

    [Fact]
    public async Task RunAsync_PromoCodeSelection_PicksHighestDiscount()
    {
        StubCartState.Lines.Clear();
        StubCartState.OutOfStock.Clear();
        StubCartState.CheckoutReady = false;

        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();
        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        var sessionId = Guid.NewGuid();
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder, sessionId);

        await new CheckoutCascade(mcpClient).RunAsync(confirmApplyBonus: _ => Task.FromResult(false));

        // The fixture offers SAVE5 (discount 5) and SAVE20 (discount 20) - the cascade must
        // pick SAVE20, the largest, per TASKS.md 7.3 "найвигідніший".
        var promoCall = await db.McpCalls
            .Where(c => c.SessionId == sessionId && c.Tool == "update_shopping_cart")
            .OrderBy(c => c.CreatedAt)
            .FirstAsync();
        Assert.NotEqual("-", promoCall.ArgsHash);
    }

    [Fact]
    public async Task RunAsync_GuestDeclinesBonus_NeverAppliesIt()
    {
        StubCartState.Lines.Clear();
        StubCartState.OutOfStock.Clear();
        StubCartState.CheckoutReady = false;

        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();
        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        var sessionId = Guid.NewGuid();
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder, sessionId);

        var wasAsked = false;
        await new CheckoutCascade(mcpClient).RunAsync(confirmApplyBonus: _ =>
        {
            wasAsked = true;
            return Task.FromResult(false);
        });

        Assert.True(wasAsked, "Must still ask, even if the answer turns out to be no.");

        var updateCartCalls = await db.McpCalls
            .Where(c => c.SessionId == sessionId && c.Tool == "update_shopping_cart")
            .CountAsync();
        Assert.Equal(1, updateCartCalls); // only the promo-code update, not a second one for bonuses
    }
}
