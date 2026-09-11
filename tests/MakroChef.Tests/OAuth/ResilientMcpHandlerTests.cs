using System.Net;
using MakroChef.Mcp;
using MakroChef.Mcp.OAuth;
using Xunit;

namespace MakroChef.Tests.OAuth;

public class ResilientMcpHandlerTests
{
    private class StubTokenProvider(string? token = "initial-token") : IMcpAuthTokenProvider
    {
        public int ForceRefreshCallCount { get; private set; }
        public string? CurrentToken { get; private set; } = token;

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(CurrentToken);

        public Task<string?> ForceRefreshAsync(CancellationToken cancellationToken = default)
        {
            ForceRefreshCallCount++;
            CurrentToken = "refreshed-token";
            return Task.FromResult<string?>(CurrentToken);
        }
    }

    [Fact]
    public async Task SendAsync_429ThenOk_RetriesAndSucceedsWithoutCallerSeeingTheFailure()
    {
        var callCount = 0;
        var fake = new FakeHttpMessageHandler(_ =>
        {
            callCount++;
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });

        var handler = new ResilientMcpHandler(new StubTokenProvider(), backoffPolicy: new BackoffPolicy(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(5)))
        {
            InnerHandler = fake,
        };
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("http://test.local/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task SendAsync_401_ForcesRefreshAndRetriesOnce()
    {
        var callCount = 0;
        var seenAuthHeaders = new List<string?>();
        var fake = new FakeHttpMessageHandler(request =>
        {
            callCount++;
            seenAuthHeaders.Add(request.Headers.Authorization?.Parameter);
            return callCount == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });

        var tokenProvider = new StubTokenProvider();
        var handler = new ResilientMcpHandler(tokenProvider) { InnerHandler = fake };
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("http://test.local/x");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, tokenProvider.ForceRefreshCallCount);
        Assert.Equal(["initial-token", "refreshed-token"], seenAuthHeaders);
    }

    [Fact]
    public async Task SendAsync_401Twice_DoesNotLoopForever_ReturnsSecondUnauthorized()
    {
        var fake = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var handler = new ResilientMcpHandler(new StubTokenProvider()) { InnerHandler = fake };
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("http://test.local/x");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_429MoreTimesThanMaxRetries_GivesUpAndReturns429()
    {
        var fake = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var handler = new ResilientMcpHandler(
            new StubTokenProvider(),
            backoffPolicy: new BackoffPolicy(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2)),
            max429Retries: 2)
        {
            InnerHandler = fake,
        };
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("http://test.local/x");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}
