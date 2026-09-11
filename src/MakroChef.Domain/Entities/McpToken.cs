namespace MakroChef.Domain.Entities;

public class McpToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required byte[] EncryptedAccessToken { get; set; }
    public required byte[] AccessTokenNonce { get; set; }
    public required byte[] EncryptedRefreshToken { get; set; }
    public required byte[] RefreshTokenNonce { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
