namespace MakroChef.Agent.Catalog;

/// <summary>TASKS.md 6.1 pool assembly inputs. DeficitCategories should include the guest's
/// usual categories plus known-deficit ones (cheese, fish, eggs, legumes); RestrictedCategories
/// come straight from get_my_food_restrictions (TASKS.md 4.1) and hard-exclude matching
/// candidates rather than merely deprioritizing them.</summary>
public record CandidatePoolRequest(
    IReadOnlyList<string> SeedProductIds,
    IReadOnlyList<string> DeficitCategories,
    IReadOnlyList<string> RestrictedCategories);
