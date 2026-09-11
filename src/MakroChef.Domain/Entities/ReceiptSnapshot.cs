namespace MakroChef.Domain.Entities;

public class ReceiptSnapshot
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string SourceOrderId { get; set; }
    public required string RawPayloadJson { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}
