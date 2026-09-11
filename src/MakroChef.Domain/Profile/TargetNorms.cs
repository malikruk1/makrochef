namespace MakroChef.Domain.Profile;

/// <summary>Household-level targets (TASKS.md 4.2: "підсумувати на домогосподарство", not per
/// person) for the solver's protein floor / sugar ceiling / kcal corridor. <see cref="Source"/>
/// must always be shown next to the numbers in the UI — "estimate" carries far less confidence
/// than "guest-stated".</summary>
public record TargetNorms(
    decimal ProteinTargetGrams,
    decimal MaxSugarGrams,
    decimal KcalMin,
    decimal KcalMax,
    string Source);
