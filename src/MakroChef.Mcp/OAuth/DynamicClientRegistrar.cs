using System.Net.Http.Json;
using MakroChef.Domain.OAuth;

namespace MakroChef.Mcp.OAuth;

public class DynamicClientRegistrar(HttpClient httpClient)
{
    public async Task<ClientRegistrationResponse> RegisterAsync(
        string registrationEndpoint,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        var request = new ClientRegistrationRequest(
            ClientName: "MakroChef",
            RedirectUris: [redirectUri],
            GrantTypes: ["authorization_code", "refresh_token"],
            ResponseTypes: ["code"]);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(registrationEndpoint, request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new DynamicClientRegistrationException($"Не вдалося звʼязатись із {registrationEndpoint}: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new DynamicClientRegistrationException(
                $"Реєстрація клієнта відхилена ({(int)response.StatusCode} {response.ReasonPhrase}): {body}");
        }

        var registration = await response.Content.ReadFromJsonAsync<ClientRegistrationResponse>(cancellationToken: cancellationToken);
        return registration ?? throw new DynamicClientRegistrationException($"{registrationEndpoint} повернув порожню відповідь.");
    }
}

public class DynamicClientRegistrationException(string message, Exception? inner = null) : Exception(message, inner);
