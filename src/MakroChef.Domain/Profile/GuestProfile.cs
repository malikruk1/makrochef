namespace MakroChef.Domain.Profile;

/// <summary>Everything TASKS.md 4.1 gathers about the guest, already parsed out of the raw MCP
/// responses. <see cref="StatedProteinTargetGrams"/> is priority 1 (TASKS.md 4.2: "гість сказав
/// сам") — set only when the guest explicitly asked for a number; leave null to fall through to
/// the calculated/default tiers.</summary>
public record GuestProfile(
    int? AgeYears,
    IReadOnlyList<FamilyMember> Family,
    IReadOnlyList<string> Restrictions,
    bool HasSavedAddress,
    decimal LoyaltyBonusBalance,
    decimal? StatedProteinTargetGrams = null)
{
    /// <summary>1.0 per adult, 0.7 per child — a simplified stand-in for per-person calorie
    /// need since we don't have (and won't invent) anyone's weight.</summary>
    public decimal AdultEquivalents => 1m + Family.Sum(f => f.IsChild ? 0.7m : 1.0m);
}
