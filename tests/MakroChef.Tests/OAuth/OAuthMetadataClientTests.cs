using System.Net;
using MakroChef.Mcp.OAuth;
using Xunit;

namespace MakroChef.Tests.OAuth;

public class OAuthMetadataClientTests
{
    [Fact]
    public async Task FetchAsync_ValidResponse_ParsesAllFields()
    {
        const string json = """
        {
          "issuer": "https://auth.silpo.ua",
          "authorization_endpoint": "https://auth.silpo.ua/authorize",
          "token_endpoint": "https://auth.silpo.ua/token",
          "registration_endpoint": "https://auth.silpo.ua/register",
          "code_challenge_methods_supported": ["S256"]
        }
        """;

        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
        var client = new OAuthMetadataClient(new HttpClient(handler));

        var metadata = await client.FetchAsync(new Uri("https://mcp.silpo.ua/mcp"));

        Assert.Equal("https://auth.silpo.ua", metadata.Issuer);
        Assert.Equal("https://auth.silpo.ua/authorize", metadata.AuthorizationEndpoint);
        Assert.Equal("https://auth.silpo.ua/token", metadata.TokenEndpoint);
        Assert.Equal("https://auth.silpo.ua/register", metadata.RegistrationEndpoint);
        Assert.Contains("S256", metadata.CodeChallengeMethodsSupported!);
    }

    [Fact]
    public async Task FetchAsync_ServerReturns404_ThrowsClearException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new OAuthMetadataClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<OAuthMetadataException>(() => client.FetchAsync(new Uri("https://mcp.silpo.ua/mcp")));
        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_MalformedJson_ThrowsClearException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json"),
        });
        var client = new OAuthMetadataClient(new HttpClient(handler));

        await Assert.ThrowsAsync<OAuthMetadataException>(() => client.FetchAsync(new Uri("https://mcp.silpo.ua/mcp")));
    }
}
