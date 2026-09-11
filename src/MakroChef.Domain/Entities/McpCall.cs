namespace MakroChef.Domain.Entities;

public class McpCall
{
    public Guid Id { get; set; }
    public Guid? SessionId { get; set; }
    public required string Tool { get; set; }
    public required string ArgsHash { get; set; }
    public required string Status { get; set; }
    public int DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
