namespace MQTTClientRx.Tests.Infrastructure;

/// <summary>
/// Polling helper for the handful of assertions that genuinely depend on real elapsed time -
/// a container restarting, a background subscription attaching.
/// </summary>
/// <remarks>
/// Anything driven by <c>IScheduler</c> or <c>TimeProvider</c> should be asserted in virtual time
/// instead; reach for this only when a real clock is unavoidable.
/// </remarks>
internal static class Poll
{
    internal static async Task UntilAsync(Func<bool> condition, TimeSpan timeout, string because)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"Timed out after {timeout} waiting for {because}.");
    }
}
