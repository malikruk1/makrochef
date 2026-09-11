using MakroChef.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MakroChef.Data;

public class EfMcpTokenStore(MakroChefDbContext db)
{
    public async Task<McpToken?> FindByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.McpTokens.SingleOrDefaultAsync(t => t.UserId == userId, cancellationToken);

    public async Task UpsertAsync(McpToken token, CancellationToken cancellationToken = default)
    {
        var existing = await FindByUserAsync(token.UserId, cancellationToken);
        if (existing is null)
        {
            token.Id = Guid.NewGuid();
            token.CreatedAt = DateTimeOffset.UtcNow;
            db.McpTokens.Add(token);
        }
        else
        {
            existing.EncryptedAccessToken = token.EncryptedAccessToken;
            existing.AccessTokenNonce = token.AccessTokenNonce;
            existing.EncryptedRefreshToken = token.EncryptedRefreshToken;
            existing.RefreshTokenNonce = token.RefreshTokenNonce;
            existing.ExpiresAt = token.ExpiresAt;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
