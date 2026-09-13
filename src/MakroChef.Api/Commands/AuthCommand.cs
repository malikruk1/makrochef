using System.Security.Cryptography;
using MakroChef.Data;
using MakroChef.Domain.Entities;
using MakroChef.Domain.OAuth;
using MakroChef.Mcp.OAuth;

namespace MakroChef.Api.Commands;

/// <summary>`dotnet run -- auth` — drives the OAuth 2.1 + PKCE flow end to end (TASKS.md 3.2).
/// Everything here is real protocol code; the only thing that can't be exercised without a
/// live Silpo account and registered client (BLOCKERS.md B-1, B-2) is actually completing the
/// browser login. Any failure — no network, no account, server unreachable — is caught here
/// and printed as one clear line, never a raw stack trace.</summary>
public static class AuthCommand
{
    public static async Task<int> RunAsync(Uri mcpBaseUri, Guid userId, EfMcpTokenStore tokenStore, TokenEncryptor tokenEncryptor)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            Console.WriteLine($"Отримую OAuth-метадані з {mcpBaseUri}...");
            var metadata = await new OAuthMetadataClient(httpClient).FetchAsync(mcpBaseUri);

            if (metadata.RegistrationEndpoint is null)
            {
                throw new InvalidOperationException(
                    "Сервер не оголосив registration_endpoint — динамічна реєстрація клієнта неможлива (потрібне рішення B-1: чи є вже client_id).");
            }

            using var redirectListener = new LoopbackRedirectListener();
            var redirectUri = redirectListener.Start();

            Console.WriteLine("Реєструю клієнта (Dynamic Client Registration)...");
            var registration = await new DynamicClientRegistrar(httpClient)
                .RegisterAsync(metadata.RegistrationEndpoint, redirectUri);

            var pkce = PkceGenerator.Generate();
            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var authorizeUrl = BuildAuthorizeUrl(metadata.AuthorizationEndpoint, registration.ClientId, redirectUri, pkce, state);

            Console.WriteLine();
            Console.WriteLine("Відкрийте це посилання в браузері та увійдіть у Сільпо (телефон + SMS-код):");
            Console.WriteLine(authorizeUrl);
            Console.WriteLine();
            Console.WriteLine("Чекаю на редірект (5 хв)...");

            var code = await redirectListener.WaitForCodeAsync(state, TimeSpan.FromMinutes(5));

            Console.WriteLine("Обмінюю код на токен...");
            var token = await new TokenExchangeClient(httpClient)
                .ExchangeCodeAsync(metadata.TokenEndpoint, registration.ClientId, code, redirectUri, pkce.CodeVerifier);

            var encryptedAccess = tokenEncryptor.Encrypt(token.AccessToken);
            var encryptedRefresh = tokenEncryptor.Encrypt(token.RefreshToken ?? "");

            await tokenStore.UpsertAsync(new McpToken
            {
                UserId = userId,
                EncryptedAccessToken = encryptedAccess.Ciphertext,
                AccessTokenNonce = encryptedAccess.Nonce,
                EncryptedRefreshToken = encryptedRefresh.Ciphertext,
                RefreshTokenNonce = encryptedRefresh.Nonce,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresInSeconds),
            });

            Console.WriteLine($"Готово. access_token отримано й збережено в Postgres (AES-GCM), дійсний {token.ExpiresInSeconds}с.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Автентифікація не вдалась: {ex.Message}");
            return 1;
        }
    }

    private static string BuildAuthorizeUrl(string authorizationEndpoint, string clientId, string redirectUri, PkcePair pkce, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["code_challenge"] = pkce.CodeChallenge,
            ["code_challenge_method"] = pkce.CodeChallengeMethod,
            ["state"] = state,
        };

        var queryString = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{authorizationEndpoint}?{queryString}";
    }
}
