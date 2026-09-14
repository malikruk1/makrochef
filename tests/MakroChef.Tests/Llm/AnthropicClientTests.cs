using MakroChef.Agent.Llm;
using Xunit;

namespace MakroChef.Tests.Llm;

public class AnthropicClientTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompleteAsync_NoApiKeyConfigured_ReturnsNullWithoutAnyNetworkCall(string? apiKey)
    {
        // A real HttpClient with no base address - if this ever attempted a network call it
        // would throw, proving the "no key -> no-op" short-circuit actually short-circuits.
        var client = new AnthropicClient(new HttpClient(), apiKey);

        var result = await client.CompleteAsync("system", "user");

        Assert.Null(result);
    }
}
