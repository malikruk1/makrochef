using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MakroChef.Agent.Llm;

/// <summary>Thin wrapper around the Claude Messages API (docs.anthropic.com/en/api/messages).
/// Uses Haiku by default - this is short, cheap text generation (name cleanup, one-sentence swap
/// explanations), never anything that needs a bigger model. <see cref="ApiKey"/> is nullable by
/// design: constructing this with a null/empty key makes every call a no-op that returns null
/// immediately, no network call attempted - matches BLOCKERS.md's "ANTHROPIC_API_KEY optional,
/// does not block core function" note.</summary>
public class AnthropicClient(HttpClient httpClient, string? apiKey, string model = "claude-haiku-4-5-20251001") : IAnthropicClient
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";

    public async Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");

            var body = JsonSerializer.Serialize(new
            {
                model,
                max_tokens = 1024,
                system = systemPrompt,
                messages = new[] { new { role = "user", content = userPrompt } },
            });
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null; // rate limit, auth failure, transient outage - never block the agent's core job over this
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            return ExtractText(responseJson);
        }
        catch (Exception)
        {
            // Network failure, timeout, malformed response - same tolerance every other
            // best-effort integration in this project already has: LLM output is decoration, not
            // a dependency the core loop can fail on.
            return null;
        }
    }

    private static string? ExtractText(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "text"
                && block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                sb.Append(text.GetString());
            }
        }

        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
