using System.Text.Json;
using MakroChef.Domain.OAuth;

namespace MakroChef.Mcp.OAuth;

public class OAuthMetadataClient(HttpClient httpClient)
{
    public async Task<OAuthServerMetadata> FetchAsync(Uri mcpServerBaseUri, CancellationToken cancellationToken = default)
    {
        var metadataUri = new Uri(mcpServerBaseUri, "/.well-known/oauth-authorization-server");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(metadataUri, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new OAuthMetadataException($"Не вдалося звʼязатись із {metadataUri}: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new OAuthMetadataException(
                $"{metadataUri} повернув {(int)response.StatusCode} {response.ReasonPhrase}. " +
                "Очікувались метадані OAuth-сервера (RFC 8414).");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var metadata = JsonSerializer.Deserialize<OAuthServerMetadata>(json)
                ?? throw new OAuthMetadataException($"{metadataUri} повернув порожню відповідь.");
            return metadata;
        }
        catch (JsonException ex)
        {
            throw new OAuthMetadataException($"Не вдалося розпарсити метадані з {metadataUri}: {ex.Message}", ex);
        }
    }
}

public class OAuthMetadataException(string message, Exception? inner = null) : Exception(message, inner);
