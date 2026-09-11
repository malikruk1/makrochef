using MakroChef.Domain.Profile;

namespace MakroChef.Agent.Profiling;

/// <summary>TASKS.md 4.2: three sources in priority order. Weight is never invented — either
/// the guest states a number, or everything is derived from calories actually seen in their
/// receipt history.</summary>
public class TargetNormsCalculator
{
    private const decimal DefaultKcalPerAdultEquivalent = 2000m;
    private const decimal ProteinShareOfCalories = 0.15m; // mid of the 1.2-1.6 g/kg guidance, expressed via calories instead of an invented weight
    private const decimal FreeSugarTargetShareOfCalories = 0.05m; // WHO: <10%, target <5% (TASKS.md 4.2)
    private const decimal KcalPerGramProtein = 4m;
    private const decimal KcalPerGramSugar = 4m;

    public TargetNorms Compute(GuestProfile profile, decimal? medianDailyKcal)
    {
        var householdKcal = medianDailyKcal ?? DefaultKcalPerAdultEquivalent * profile.AdultEquivalents;
        var source = medianDailyKcal is not null ? "calculated" : "estimate";

        var proteinGrams = profile.StatedProteinTargetGrams
            ?? householdKcal * ProteinShareOfCalories / KcalPerGramProtein;
        if (profile.StatedProteinTargetGrams is not null)
        {
            source = "guest-stated";
        }

        var maxSugarGrams = householdKcal * FreeSugarTargetShareOfCalories / KcalPerGramSugar;

        return new TargetNorms(
            ProteinTargetGrams: Math.Round(proteinGrams, 1),
            MaxSugarGrams: Math.Round(maxSugarGrams, 1),
            KcalMin: Math.Round(householdKcal * 0.9m, 0),
            KcalMax: Math.Round(householdKcal * 1.1m, 0),
            Source: source);
    }
}
