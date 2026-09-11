using System.Net.Http.Json;
using MakroChef.Domain.OAuth;

namespace MakroChef.Mcp.OAuth;

/// <summary>RFC 6749 §4.1.3 authorization_code grant (with PKCE) and §6 refresh_token grant.</summary>
public class TokenExchangeClient(HttpClient httpClient)
{
    public Task<TokenResponse> ExchangeCodeAsync(
        string tokenEndpoint, string clientId, string code, string redirectUri, string codeVerifier,
        CancellationToken cancellationToken = default) =>
        PostFormAsync(tokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier,
        }, cancellationToken);

    public Task<TokenResponse> RefreshAsync(
        string tokenEndpoint, string clientId, string refreshToken,
        CancellationToken cancellationToken = default) =>
        PostFormAsync(tokenEndpoint, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        }, cancellationToken);

    private async Task<TokenResponse> PostFormAsync(
        string tokenEndpoint, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new TokenExchangeException($"Не вдалося звʼязатись із {tokenEndpoint}: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new TokenExchangeException($"{tokenEndpoint} повернув {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
        return token ?? throw new TokenExchangeException($"{tokenEndpoint} повернув порожню відповідь.");
    }
}

public class TokenExchangeException(string message, Exception? inner = null) : Exception(message, inner);
