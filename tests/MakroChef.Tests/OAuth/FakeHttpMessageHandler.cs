namespace MakroChef.Tests.OAuth;

/// <summary>Deterministic HttpMessageHandler stand-in — this is the one place TASKS.md's
/// "don't mock MCP outside unit tests" rule intends: pure HTTP-transport unit tests, not a
/// mock of the Silpo MCP server's business behavior.</summary>
public class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(responder(request));
}
