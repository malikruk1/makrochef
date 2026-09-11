namespace MakroChef.Domain.Profile;

public record FamilyMember(int? AgeYears)
{
    /// <summary>TASKS.md 4.2: children count differently toward household norms, not "one more adult".</summary>
    public bool IsChild => AgeYears is not null && AgeYears < 14;
}
