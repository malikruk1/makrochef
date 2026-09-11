using System.Net.Http.Headers;

namespace MakroChef.Mcp;

/// <summary>Attaches the current bearer token to every outgoing request. A handler (not a
/// static header) so a refreshed token is picked up on the very next call without recreating
/// the transport/client.</summary>
public class BearerTokenHandler(IMcpAuthTokenProvider tokenProvider) : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
