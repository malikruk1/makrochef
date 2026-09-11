using MakroChef.Agent.Profiling;
using MakroChef.Domain.Profile;
using Xunit;

namespace MakroChef.Tests.Profiling;

/// <summary>Gate 4: three fixtures (single, family with a child, guest with restrictions) give
/// different norms; consumed/target/deficit reconciles; source is always visible.</summary>
public class TargetNormsCalculatorTests
{
    private readonly TargetNormsCalculator _calculator = new();

    [Fact]
    public void Compute_SingleAdult_UsesCalculatedSource()
    {
        var profile = new GuestProfile(AgeYears: 30, Family: [], Restrictions: [], HasSavedAddress: true, LoyaltyBonusBalance: 0);

        var norms = _calculator.Compute(profile, medianDailyKcal: 2100m);

        Assert.Equal("calculated", norms.Source);
        Assert.True(norms.ProteinTargetGrams > 0);
        Assert.True(norms.MaxSugarGrams > 0);
        Assert.True(norms.KcalMin < norms.KcalMax);
    }

    [Fact]
    public void Compute_FamilyWithChild_HasHigherNormsThanSingleAdult_ButLowerThanTwoAdults()
    {
        var single = new GuestProfile(AgeYears: 35, Family: [], Restrictions: [], HasSavedAddress: true, LoyaltyBonusBalance: 0);
        var familyWithChild = new GuestProfile(
            AgeYears: 35, Family: [new FamilyMember(AgeYears: 8)], Restrictions: [], HasSavedAddress: true, LoyaltyBonusBalance: 0);

        // Same median kcal for both fixtures would hide the household-size effect the calculator
        // is supposed to model, so let the estimate tier (no measured kcal yet) show it instead.
        var singleNorms = _calculator.Compute(single, medianDailyKcal: null);
        var familyNorms = _calculator.Compute(familyWithChild, medianDailyKcal: null);

        Assert.Equal("estimate", singleNorms.Source);
        Assert.Equal("estimate", familyNorms.Source);
        Assert.True(familyNorms.ProteinTargetGrams > singleNorms.ProteinTargetGrams);
        Assert.True(familyNorms.KcalMax > singleNorms.KcalMax);

        // A child counts for less than a full adult (TASKS.md 4.2), not "family = 2x single".
        Assert.True(familyNorms.ProteinTargetGrams < singleNorms.ProteinTargetGrams * 2);
    }

    [Fact]
    public void Compute_GuestWithRestrictionsAndStatedProteinTarget_UsesGuestStatedSource()
    {
        var profile = new GuestProfile(
            AgeYears: 28,
            Family: [],
            Restrictions: ["риба", "горіхи"],
            HasSavedAddress: false,
            LoyaltyBonusBalance: 150,
            StatedProteinTargetGrams: 100m);

        var norms = _calculator.Compute(profile, medianDailyKcal: 1900m);

        Assert.Equal("guest-stated", norms.Source);
        Assert.Equal(100m, norms.ProteinTargetGrams);
        Assert.NotEmpty(profile.Restrictions);
    }

    [Fact]
    public void Compute_ThreeFixtures_ProduceDistinctNorms()
    {
        var single = new GuestProfile(AgeYears: 35, Family: [], Restrictions: [], HasSavedAddress: true, LoyaltyBonusBalance: 0);
        var familyWithChild = new GuestProfile(
            AgeYears: 35, Family: [new FamilyMember(AgeYears: 8)], Restrictions: [], HasSavedAddress: true, LoyaltyBonusBalance: 0);
        var withRestrictions = new GuestProfile(
            AgeYears: 28, Family: [], Restrictions: ["риба"], HasSavedAddress: false, LoyaltyBonusBalance: 150, StatedProteinTargetGrams: 100m);

        var norms = new[]
        {
            _calculator.Compute(single, 2100m),
            _calculator.Compute(familyWithChild, 3200m),
            _calculator.Compute(withRestrictions, 1900m),
        };

        Assert.Equal(3, norms.Select(n => n.ProteinTargetGrams).Distinct().Count());
    }
}

public class NutritionGapReportTests
{
    [Theory]
    [InlineData(60, 100, 40, 60.0)]
    [InlineData(100, 100, 0, 100.0)]
    [InlineData(130, 100, 0, 100.0)] // over-consumption: deficit floors at 0, coverage caps at 100%
    [InlineData(0, 0, 0, 100.0)] // no target set (edge case) - nothing to be deficient in
    public void Deficit_And_Coverage_Reconcile_WithConsumedPlusTarget(
        decimal consumed, decimal target, decimal expectedDeficit, double expectedCoverage)
    {
        var report = new NutritionGapReport(consumed, target, NormSource: "calculated");

        Assert.Equal(expectedDeficit, report.ProteinDeficitGrams);
        Assert.Equal(expectedCoverage, report.CoveragePercent, precision: 1);

        if (consumed <= target)
        {
            Assert.Equal(target, consumed + report.ProteinDeficitGrams);
        }
    }
}
