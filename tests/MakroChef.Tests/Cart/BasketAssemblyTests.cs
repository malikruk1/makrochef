using MakroChef.Agent.Cart;
using MakroChef.Agent.Tracing;
using MakroChef.Data;
using MakroChef.Domain.Solver;
using MakroChef.Mcp;
using MakroChef.Nutrition;
using MakroChef.Tests.Stubs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MakroChef.Tests.Cart;

public class BasketAssemblyTests
{
    [Fact]
    public async Task AssembleAsync_EmptyCart_AddsLinesWithoutAskingToClear_AndRereadsAfterward()
    {
        StubCartState.Lines.Clear();
        StubCartState.OutOfStock.Clear();

        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();
        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);

        var assembler = new BasketAssembler(mcpClient, StubSession.Default);
        var askedToClear = false;

        var cart = await assembler.AssembleAsync(
            [new BasketLine("sku1", 2)],
            confirmClearIfNotEmpty: () => { askedToClear = true; return Task.FromResult(true); });

        Assert.False(askedToClear, "Must not ask to clear an already-empty cart.");
        Assert.Single(cart.Lines);
        Assert.Equal("sku1", cart.Lines[0].ProductId);
        Assert.Equal(2, cart.Lines[0].Quantity);
        Assert.Empty(cart.Validations);
    }

    [Fact]
    public async Task AssembleAsync_NonEmptyCart_AsksBeforeClearing()
    {
        StubCartState.Lines.Clear();
        StubCartState.Lines["leftover"] = 1;
        StubCartState.OutOfStock.Clear();

        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();
        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);

        var assembler = new BasketAssembler(mcpClient, StubSession.Default);
        var askedToClear = false;

        var cart = await assembler.AssembleAsync(
            [new BasketLine("sku1", 1)],
            confirmClearIfNotEmpty: () => { askedToClear = true; return Task.FromResult(true); });

        Assert.True(askedToClear);
        Assert.DoesNotContain(cart.Lines, l => l.ProductId == "leftover");
    }

    /// <summary>Gate 7.2 — THE key point: an out-of-stock item triggers a full re-solve, not a
    /// 1-for-1 substitution. The new cart ends up with a completely different product (not the
    /// "obvious" same-category replacement) because re-solving the whole pool found it cheaper -
    /// proof the solver actually re-ran rather than the code just swapping one SKU for another.</summary>
    [Fact]
    public async Task ReoptimizeAsync_ArtificiallyOutOfStockItem_FullyResolvesBudgetKeptDeficitNotWorse()
    {
        StubCartState.Lines.Clear();
        StubCartState.OutOfStock.Clear();

        await using var stubServer = new StubMcpServer();
        await stubServer.StartAsync();
        await using var db = new MakroChefDbContext(
            new DbContextOptionsBuilder<MakroChefDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var recorder = new EfMcpCallRecorder(db);
        await using var mcpClient = new MakroChefMcpClient(stubServer.Endpoint, new NullMcpAuthTokenProvider(), recorder);
        var resolver = new ExactMcpNutritionResolver(mcpClient, StubSession.Default);
        var solver = new LoggingBasketSolver(new MakroChef.Solver.BasketSolver(), recorder);
        var assembler = new BasketAssembler(mcpClient, StubSession.Default);
        var reoptimizer = new ReoptimizationService(mcpClient, resolver, solver, assembler, StubSession.Default);

        // Hand-built pool: test_cheese is the cheapest per gram, so it dominates the initial
        // solve. Its only listed replacement (cheese_b, via get_replacements) is *not* the
        // cheapest option once test_cheese is gone - test_fish is - so a correct re-solve should
        // end up on test_fish, not on the "obvious" same-category cheese_b swap.
        var candidates = new List<Candidate>
        {
            new("test_cheese", "сир", PriceKopecks: 4000, ProteinMg: 50_000, SugarMg: 0, Kcal: 100, Restricted: false),
            new("test_fish", "риба", PriceKopecks: 6000, ProteinMg: 60_000, SugarMg: 0, Kcal: 100, Restricted: false),
            new("test_eggs", "яйця", PriceKopecks: 9000, ProteinMg: 30_000, SugarMg: 0, Kcal: 50, Restricted: false),
        };
        var request = new SolverRequest(
            Candidates: candidates,
            TargetProteinMg: 100_000,
            MaxSugarMg: 100_000,
            KcalMin: 10,
            KcalMax: 100_000,
            BaselineCostKopecks: 20_000);

        var initialSolve = await solver.SolveAsync(request);
        Assert.True(initialSolve.Success);
        await assembler.AddOrUpdateAsync(initialSolve.Lines);
        var initialCart = await assembler.GetCartAsync();

        Assert.Equal(100_000, initialSolve.TotalProteinMg);
        Assert.Equal(8_000, initialSolve.TotalCostKopecks);
        Assert.Contains(initialCart.Lines, l => l.ProductId == "test_cheese");

        // Simulate the item going out of stock, as --simulate-out-of-stock=test_cheese would
        // on the CLI (TASKS.md 7.2 gate note).
        StubCartState.OutOfStock.Add("test_cheese");
        var cartWithIssue = await assembler.GetCartAsync();
        Assert.Contains(cartWithIssue.Validations, v => v.ProductId == "test_cheese" && v.IsOutOfStock);

        var result = await reoptimizer.ReoptimizeAsync(request, cartWithIssue);

        Assert.True(result.FullyResolved);
        Assert.Empty(result.DegradedNotes);

        var oldProductIds = initialCart.Lines.Select(l => l.ProductId).ToHashSet();
        var newProductIds = result.FinalCart.Lines.Select(l => l.ProductId).ToHashSet();
        var changedCount = oldProductIds.Except(newProductIds).Count() + newProductIds.Except(oldProductIds).Count();
        Assert.True(changedCount > 1, $"Expected the basket to change by more than one line, changed: {changedCount}");

        // The re-solve should have picked test_fish, not the "obvious" cheese_b swap -
        // exactly the point 7.2 makes about not doing a 1-for-1 substitution.
        Assert.Contains(newProductIds, id => id == "test_fish");
        Assert.DoesNotContain(newProductIds, id => id == "cheese_b");

        var finalCandidatesById = new Dictionary<string, Candidate>
        {
            ["test_fish"] = candidates.Single(c => c.ProductId == "test_fish"),
        };
        var newTotalCost = result.FinalCart.Lines.Sum(l => finalCandidatesById.TryGetValue(l.ProductId, out var c) ? c.PriceKopecks * l.Quantity : 0);
        var newTotalProtein = result.FinalCart.Lines.Sum(l => finalCandidatesById.TryGetValue(l.ProductId, out var c) ? c.ProteinMg * l.Quantity : 0);

        Assert.True(newTotalCost <= request.BaselineCostKopecks, "Budget must be kept.");
        Assert.True(newTotalProtein >= request.TargetProteinMg, "Deficit must not have grown.");
    }
}
