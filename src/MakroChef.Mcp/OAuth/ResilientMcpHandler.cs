using System.Net;
using System.Net.Http.Headers;

namespace MakroChef.Mcp.OAuth;

/// <summary>Attaches the bearer token and per-user rate-limit cookie to every request, and
/// implements the TASKS.md 3.2 error table:
/// 401 invalid_token -> force refresh, retry once; 429 -> exponential backoff with jitter,
/// bounded retries; 403 -> passed through as-is (soft degradation is the caller's call, not
/// the transport's); everything else passed through unchanged.</summary>
public class ResilientMcpHandler(
    IMcpAuthTokenProvider tokenProvider,
    string? userId = null,
    BackoffPolicy? backoffPolicy = null,
    int max429Retries = 4)
    : DelegatingHandler(new HttpClientHandler())
{
    private readonly BackoffPolicy _backoff = backoffPolicy ?? new BackoffPolicy();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var originalRequest = await BufferedRequest.CaptureAsync(request, cancellationToken);
        var attempt = 0;
        var didForceRefresh = false;

        while (true)
        {
            var attemptRequest = originalRequest.ToHttpRequestMessage();
            await AttachAuthAsync(attemptRequest, cancellationToken);

            var response = await base.SendAsync(attemptRequest, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < max429Retries)
            {
                var delay = RetryAfterOrBackoff(response, attempt);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
                attempt++;
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && !didForceRefresh)
            {
                didForceRefresh = true;
                var refreshed = await tokenProvider.ForceRefreshAsync(cancellationToken);
                if (refreshed is not null)
                {
                    response.Dispose();
                    continue;
                }
            }

            return response;
        }
    }

    private TimeSpan RetryAfterOrBackoff(HttpResponseMessage response, int attempt) =>
        response.Headers.RetryAfter?.Delta ?? _backoff.ComputeDelay(attempt);

    private async Task AttachAuthAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (!string.IsNullOrEmpty(userId))
        {
            request.Headers.Add("Cookie", $"mcp-user={userId}");
        }
    }

    /// <summary>HttpRequestMessage can only be sent once; this captures method/URI/headers/body
    /// up front so each retry attempt gets its own fresh instance.</summary>
    private sealed class BufferedRequest(
        HttpMethod method,
        Uri? requestUri,
        List<(string Name, IEnumerable<string> Values)> headers,
        byte[]? body,
        string? contentType)
    {
        private HttpMethod Method { get; } = method;
        private Uri? RequestUri { get; } = requestUri;
        private List<(string Name, IEnumerable<string> Values)> Headers { get; } = headers;
        private byte[]? Body { get; } = body;
        private string? ContentType { get; } = contentType;

        public static async Task<BufferedRequest> CaptureAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[]? body = null;
            string? contentType = null;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                contentType = request.Content.Headers.ContentType?.ToString();
            }

            return new BufferedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Select(h => (h.Key, h.Value)).ToList(),
                body,
                contentType);
        }

        public HttpRequestMessage ToHttpRequestMessage()
        {
            var request = new HttpRequestMessage(Method, RequestUri);
            foreach (var (name, values) in Headers)
            {
                request.Headers.TryAddWithoutValidation(name, values);
            }

            if (Body is not null)
            {
                request.Content = new ByteArrayContent(Body);
                if (ContentType is not null)
                {
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContentType);
                }
            }

            return request;
        }
    }
}
