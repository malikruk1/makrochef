namespace MakroChef.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string SilpoUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
