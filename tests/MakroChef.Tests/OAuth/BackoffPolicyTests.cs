using MakroChef.Mcp.OAuth;
using Xunit;

namespace MakroChef.Tests.OAuth;

public class BackoffPolicyTests
{
    [Fact]
    public void ComputeDelay_GrowsExponentiallyWithAttempt()
    {
        // Random.Shared.NextDouble() varies, so pin it to always return 1.0 (max jitter)
        // to make the exponential growth itself deterministic and assertable.
        var alwaysMaxJitter = new AlwaysMaxRandom();
        var policy = new BackoffPolicy(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(30), alwaysMaxJitter);

        var delay0 = policy.ComputeDelay(0);
        var delay1 = policy.ComputeDelay(1);
        var delay2 = policy.ComputeDelay(2);

        Assert.Equal(TimeSpan.FromMilliseconds(100), delay0);
        Assert.Equal(TimeSpan.FromMilliseconds(200), delay1);
        Assert.Equal(TimeSpan.FromMilliseconds(400), delay2);
    }

    [Fact]
    public void ComputeDelay_NeverExceedsMaxDelay_EvenAtHighAttempts()
    {
        var alwaysMaxJitter = new AlwaysMaxRandom();
        var policy = new BackoffPolicy(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(30), alwaysMaxJitter);

        var delay = policy.ComputeDelay(20);

        Assert.Equal(TimeSpan.FromSeconds(30), delay);
    }

    [Fact]
    public void ComputeDelay_HasJitter_TwoCallsSameAttemptCanDiffer()
    {
        var policy = new BackoffPolicy(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(30));

        var delays = Enumerable.Range(0, 20).Select(_ => policy.ComputeDelay(3)).Distinct().ToList();

        Assert.True(delays.Count > 1, "Expected jitter to produce varying delays across calls.");
    }

    [Fact]
    public void ComputeDelay_NegativeAttempt_Throws()
    {
        var policy = new BackoffPolicy();
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.ComputeDelay(-1));
    }

    private class AlwaysMaxRandom : Random
    {
        public override double NextDouble() => 1.0;
    }
}
