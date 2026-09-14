namespace MakroChef.Domain.Profile;

public record FamilyMember(int? AgeYears, bool? ForcedIsChild = null)
{
    /// <summary>TASKS.md 4.2: children count differently toward household norms, not "one more adult".
    /// Confirmed live (2026-09-14): silpo_get_my_family carries no age at all on "members" (adult
    /// household members) — child/adult status there comes from which array the entry was found
    /// in ("children" vs "members"), not an inferred age threshold. ForcedIsChild lets
    /// GuestContextCollector record that real distinction directly instead of guessing from a
    /// field that doesn't exist.</summary>
    public bool IsChild => ForcedIsChild ?? (AgeYears is not null && AgeYears < 14);
}
