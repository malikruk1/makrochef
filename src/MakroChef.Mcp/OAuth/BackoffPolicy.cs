namespace MakroChef.Mcp.OAuth;

/// <summary>Exponential backoff with full jitter (AWS-style) for 429 responses
/// (TASKS.md 3.2). <see cref="Random"/> is injectable so delay computation is testable
/// without flakiness.</summary>
public class BackoffPolicy(TimeSpan baseDelay, TimeSpan maxDelay, Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;

    public BackoffPolicy() : this(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(30))
    {
    }

    /// <param name="attempt">0-based retry attempt number.</param>
    public TimeSpan ComputeDelay(int attempt)
    {
        if (attempt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt must be >= 0.");
        }

        var exponentialMs = baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var cappedMs = Math.Min(exponentialMs, maxDelay.TotalMilliseconds);
        var jitteredMs = _random.NextDouble() * cappedMs;
        return TimeSpan.FromMilliseconds(jitteredMs);
    }
}
