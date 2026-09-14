namespace MakroChef.Agent.Llm;

/// <summary>TASKS.md 0.2: the LLM never picks products — the solver does. This interface's only
/// two jobs, matching that boundary exactly: normalize a messy/abbreviated receipt name into a
/// clean human-readable one, and produce a short natural-language explanation for a swap the
/// solver already decided on. Never used for anything else. Optional throughout: every caller
/// must work correctly (with a plain deterministic fallback) when no client is configured
/// (ANTHROPIC_API_KEY unset) or when a call fails - the agent's core job never depends on it.</summary>
public interface IAnthropicClient
{
    /// <summary>Returns null on any failure (missing key, network error, unexpected response) -
    /// callers must always have a non-LLM fallback ready, never surface this as a hard error.</summary>
    Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
