using Xunit;

namespace Moltaro.OnecOutpost.TestSupport;

// The times that the tests start their clocks at, the times they let pass or wait for, and the waits on a fake clock
// and on a task that every helper of a test shares.
internal static class TestTimes
{
    // The start of most runs of the agent in these tests.
    public static readonly DateTimeOffset Start = new(2026, 11, 22, 10, 15, 0, 123, TimeSpan.Zero);

    // The longest real time a test waits for an event that the code under test makes well within it, such as a timer
    // armed or the end of a run once the fake clock passed its run limit: reached only when the event never comes, so
    // that the test fails instead of hanging.
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    // A tenth of the default retryDelayMax of policy.json: the agent's first delay after a lost answer, to a poll or to
    // an outcome (§9).
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(3);

    // What a test moves a clock by, or checks a timer one before, to state that a time is exact.
    public static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    // Completes once the code under test has armed a timer of the clock due at or before dueBy.
    public static Task WaitForTimerAsync(FakeClock clock, DateTimeOffset dueBy)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return clock.WaitForTimerAsync(dueBy, cancellationToken).WaitAsync(Deadline, cancellationToken);
    }

    // Waits until the code under test, which the message names as the waiter, has armed a timer due by the delay from
    // now, states that none is due a millisecond earlier, and moves the clock by the delay.
    public static async Task PassExactlyAsync(FakeClock clock, TimeSpan delay, string waiter)
    {
        DateTimeOffset now = clock.GetUtcNow();

        await WaitForTimerAsync(clock, now + delay);
        Assert.False(clock.HasTimerDueBy(now + delay - Millisecond), $"The {waiter} waits less than {delay}.");

        clock.Advance(delay);
    }

    // The result of a task, which has to end within the deadline of the tests.
    public static Task<T> EndOf<T>(Task<T> task)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return task.WaitAsync(Deadline, cancellationToken);
    }
}
