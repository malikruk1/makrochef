using System.Diagnostics;
using MakroChef.Domain.Solver;
using MakroChef.Solver;
using Xunit;

namespace MakroChef.Tests;

public class SolverTests
{
    private static Candidate Candidate(string id, string category, long priceKopecks, long proteinMg, long sugarMg, long kcal, bool restricted = false) =>
        new(id, category, priceKopecks, proteinMg, sugarMg, kcal, restricted);

    [Fact]
    public void Solve_NormalCase_FindsFeasibleBasketWithinBudget()
    {
        var candidates = new List<Candidate>
        {
            Candidate("cheese", "dairy", 8000, 25_000, 500, 350),
            Candidate("yogurt", "dairy", 3000, 10_000, 8_000, 120),
            Candidate("eggs", "protein", 5000, 13_000, 100, 155),
            Candidate("chicken", "protein", 12000, 31_000, 0, 165),
            Candidate("bread", "bakery", 2500, 8_000, 3_000, 265),
        };

        var request = new SolverRequest(
            Candidates: candidates,
            TargetProteinMg: 80_000,
            MaxSugarMg: 30_000,
            KcalMin: 1_000,
            KcalMax: 3_000,
            BaselineCostKopecks: 100_000);

        var solver = new BasketSolver();
        var result = solver.Solve(request);

        Assert.True(result.Success);
        Assert.Empty(result.Relaxed);
        Assert.True(result.TotalProteinMg >= request.TargetProteinMg);
        Assert.True(result.TotalSugarMg <= request.MaxSugarMg);
        Assert.True(result.TotalCostKopecks <= request.BaselineCostKopecks);
        Assert.NotEmpty(result.Lines);
    }

    [Fact]
    public void Solve_InfeasibleCase_RelaxesConstraintsInFixedOrderAndReportsIt()
    {
        // A single, very expensive, very sugary product: no combination fits target
        // protein under the original sugar/kcal/budget caps, forcing relaxation.
        var candidates = new List<Candidate>
        {
            Candidate("sugary_protein_bar", "snacks", 50_000, 20_000, 40_000, 900),
        };

        var request = new SolverRequest(
            Candidates: candidates,
            TargetProteinMg: 80_000,
            MaxSugarMg: 10_000,
            KcalMin: 100,
            KcalMax: 500,
            BaselineCostKopecks: 40_000);

        var solver = new BasketSolver();
        var result = solver.Solve(request);

        // Even after relaxing sugar, kcal, and +10% budget, 4 units (protein cap) can't
        // reach 80g protein from a single 20g-protein candidate with MaxUnits=4 — so this
        // stays infeasible, but must report every relaxation attempted, not throw.
        Assert.False(result.Success);
        Assert.Equal(3, result.Relaxed.Count);
        Assert.Contains(result.Relaxed, r => r.StartsWith("sugar ceiling"));
        Assert.Contains(result.Relaxed, r => r.StartsWith("kcal corridor"));
        Assert.Contains(result.Relaxed, r => r.StartsWith("budget"));
    }

    [Fact]
    public void Solve_RestrictedCandidate_IsNeverIncluded()
    {
        var candidates = new List<Candidate>
        {
            Candidate("peanuts", "snacks", 4000, 25_000, 500, 560, restricted: true),
            Candidate("chicken", "protein", 12000, 31_000, 0, 165),
        };

        var request = new SolverRequest(
            Candidates: candidates,
            TargetProteinMg: 30_000,
            MaxSugarMg: 30_000,
            KcalMin: 100,
            KcalMax: 3_000,
            BaselineCostKopecks: 100_000);

        var solver = new BasketSolver();
        var result = solver.Solve(request);

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Lines, l => l.ProductId == "peanuts");
    }

    [Fact]
    public void Solve_ThreeHundredCandidates_CompletesUnderTwoSeconds()
    {
        var random = new Random(42);
        var candidates = new List<Candidate>();
        for (var i = 0; i < 300; i++)
        {
            candidates.Add(new Candidate(
                ProductId: $"p{i}",
                Category: $"cat{i % 20}",
                PriceKopecks: random.Next(500, 20_000),
                ProteinMg: random.Next(0, 30_000),
                SugarMg: random.Next(0, 20_000),
                Kcal: random.Next(50, 600),
                Restricted: false));
        }

        var request = new SolverRequest(
            Candidates: candidates,
            TargetProteinMg: 100_000,
            MaxSugarMg: 50_000,
            KcalMin: 1_000,
            KcalMax: 4_000,
            BaselineCostKopecks: 150_000);

        var solver = new BasketSolver();
        var stopwatch = Stopwatch.StartNew();
        var result = solver.Solve(request);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed.TotalSeconds < 2.0, $"Took {stopwatch.Elapsed.TotalSeconds:F2}s");
    }
}
