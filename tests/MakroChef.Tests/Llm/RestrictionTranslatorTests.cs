using MakroChef.Agent.Llm;
using Xunit;

namespace MakroChef.Tests.Llm;

/// <summary>TASKS.md 0.2's third LLM job: translate free text like "без риби" into structured
/// restriction categories. Never touches candidate selection - only produces the strings the
/// solver's existing restriction filter already knows how to consume. No test calls the real
/// Claude API - a fake IAnthropicClient stands in.</summary>
public class RestrictionTranslatorTests
{
    [Fact]
    public async Task TranslateAsync_NoLlmConfigured_FallsBackToKeywordMatch()
    {
        var translator = new RestrictionTranslator(new FakeAnthropicClient(response: null));

        var result = await translator.TranslateAsync("без риби, будь ласка");

        Assert.Contains("риба", result);
    }

    [Fact]
    public async Task TranslateAsync_LlmReturnsValidJsonArray_UsesLlmResult()
    {
        var translator = new RestrictionTranslator(new FakeAnthropicClient(response: """["горіхи", "глютен"]"""));

        var result = await translator.TranslateAsync("у дитини алергія на горіхи і глютен");

        Assert.Equal(["горіхи", "глютен"], result);
    }

    [Fact]
    public async Task TranslateAsync_LlmReturnsGarbage_FallsBackToKeywordMatch()
    {
        var translator = new RestrictionTranslator(new FakeAnthropicClient(response: "not json"));

        var result = await translator.TranslateAsync("не їмо м'ясо");

        Assert.Contains("м'ясо", result);
    }

    [Fact]
    public async Task TranslateAsync_EmptyText_ReturnsEmpty()
    {
        var translator = new RestrictionTranslator(new FakeAnthropicClient(response: "irrelevant"));

        var result = await translator.TranslateAsync("");

        Assert.Empty(result);
    }

    [Fact]
    public async Task TranslateAsync_UnrecognizedTextNoLlm_ReturnsEmpty()
    {
        var translator = new RestrictionTranslator(new FakeAnthropicClient(response: null));

        var result = await translator.TranslateAsync("хочу більше знижок");

        Assert.Empty(result);
    }

    private sealed class FakeAnthropicClient(string? response) : IAnthropicClient
    {
        public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
            Task.FromResult(response);
    }
}
