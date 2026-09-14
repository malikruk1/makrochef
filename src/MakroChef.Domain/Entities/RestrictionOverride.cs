namespace MakroChef.Domain.Entities;

/// <summary>A restriction the guest added via free-text (TASKS.md 0.2: "перекладає «без риби» у
/// зміну обмежень") on top of whatever MCP itself reports. Additive only - there is no MCP tool
/// to remove a restriction, and this table never removes one either, only adds.</summary>
public class RestrictionOverride
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Restriction { get; set; }
    public required string SourceText { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
