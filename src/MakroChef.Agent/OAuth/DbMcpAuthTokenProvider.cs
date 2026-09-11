using MakroChef.Domain.Entities;
using MakroChef.Data;

namespace MakroChef.Mcp.OAuth;

/// <summary>Reads the MCP token from Postgres, decrypts it, and transparently refreshes it
/// when it's close to expiring — the rest of the app never touches raw tokens or the OAuth
/// flow directly (TASKS.md 3.2, error table row "401 invalid_token").</summary>
public class DbMcpAuthTokenProvider(
    EfMcpTokenStore tokenStore,
    TokenEncryptor tokenEncryptor,
    TokenExchangeClient exchangeClient,
    string tokenEndpoint,
    string clientId,
    Guid userId) : IMcpAuthTokenProvider
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var stored = await tokenStore.FindByUserAsync(userId, cancellationToken);
        if (stored is null)
        {
            return null; // No token yet — caller must run the `auth` CLI command first.
        }

        if (stored.ExpiresAt - DateTimeOffset.UtcNow > RefreshMargin)
        {
            return tokenEncryptor.Decrypt(new EncryptedToken(stored.EncryptedAccessToken, stored.AccessTokenNonce));
        }

        return await RefreshAsync(stored, cancellationToken);
    }

    private async Task<string> RefreshAsync(McpToken stored, CancellationToken cancellationToken)
    {
        var refreshToken = tokenEncryptor.Decrypt(new EncryptedToken(stored.EncryptedRefreshToken, stored.RefreshTokenNonce));
        var refreshed = await exchangeClient.RefreshAsync(tokenEndpoint, clientId, refreshToken, cancellationToken);

        var encryptedAccess = tokenEncryptor.Encrypt(refreshed.AccessToken);
        var encryptedRefresh = tokenEncryptor.Encrypt(refreshed.RefreshToken ?? refreshToken);

        await tokenStore.UpsertAsync(new McpToken
        {
            UserId = userId,
            EncryptedAccessToken = encryptedAccess.Ciphertext,
            AccessTokenNonce = encryptedAccess.Nonce,
            EncryptedRefreshToken = encryptedRefresh.Ciphertext,
            RefreshTokenNonce = encryptedRefresh.Nonce,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresInSeconds),
        }, cancellationToken);

        return refreshed.AccessToken;
    }
}
