using MakroChef.Agent.Llm;
using MakroChef.Domain.Catalog;
using Xunit;

namespace MakroChef.Tests.Llm;

/// <summary>TASKS.md 0.2: the LLM only ever narrates a swap the solver already decided on - never
/// picks or scores anything, and must never be a hard dependency. These tests never call the
/// real Claude API (no ANTHROPIC_API_KEY involved) - a fake IAnthropicClient stands in.</summary>
public class SwapExplainerTests
{
    private static readonly ProductSwap SampleSwap = new(
        OldProductId: "yogurt_x",
        NewProductId: "yogurt_y",
        ProteinDeltaGrams: 9,
        SugarDeltaGrams: -11,
        PriceDeltaKopecks: -300,
        OnPromotion: true,
        OldName: "Йогурт Активіа 2.9%",
        NewName: "Йогурт Danone Protein");

    [Fact]
    public async Task ExplainAsync_NoLlmConfigured_FallsBackToDeterministicTemplate()
    {
        var explainer = new SwapExplainer(new FakeAnthropicClient(response: null));

        var result = await explainer.ExplainAsync([SampleSwap]);

        Assert.Single(result);
        Assert.Same(SampleSwap, result[0].Swap);
        Assert.Contains("+9", result[0].Explanation);
        Assert.Contains("-11", result[0].Explanation.Replace("−", "-"));
    }

    [Fact]
    public async Task ExplainAsync_LlmReturnsValidJsonArray_UsesLlmText()
    {
        var explainer = new SwapExplainer(new FakeAnthropicClient(response: """["Більше білка і менше цукру за ту саму ціну."]"""));

        var result = await explainer.ExplainAsync([SampleSwap]);

        Assert.Single(result);
        Assert.Equal("Більше білка і менше цукру за ту саму ціну.", result[0].Explanation);
    }

    [Fact]
    public async Task ExplainAsync_LlmWrapsJsonInProse_StillExtractsIt()
    {
        var explainer = new SwapExplainer(new FakeAnthropicClient(
            response: "Ось пояснення:\n```json\n[\"Краще для БЖВ.\"]\n```"));

        var result = await explainer.ExplainAsync([SampleSwap]);

        Assert.Equal("Краще для БЖВ.", result[0].Explanation);
    }

    [Fact]
    public async Task ExplainAsync_LlmReturnsGarbage_FallsBackPerSwap()
    {
        var explainer = new SwapExplainer(new FakeAnthropicClient(response: "not json at all"));

        var result = await explainer.ExplainAsync([SampleSwap]);

        Assert.Single(result);
        Assert.Contains("+9", result[0].Explanation);
    }

    [Fact]
    public async Task ExplainAsync_EmptySwapList_ReturnsEmpty()
    {
        var explainer = new SwapExplainer(new FakeAnthropicClient(response: "irrelevant"));

        var result = await explainer.ExplainAsync([]);

        Assert.Empty(result);
    }

    private sealed class FakeAnthropicClient(string? response) : IAnthropicClient
    {
        public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default) =>
            Task.FromResult(response);
    }
}
