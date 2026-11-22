namespace Moltaro.OnecOutpost.Agent.Tests;

// The times that the tests start their clocks at.
internal static class TestTimes
{
    // The start of most runs of the agent in these tests.
    public static readonly DateTimeOffset Start = new(2026, 11, 22, 10, 15, 0, 123, TimeSpan.Zero);

    // The longest real time a test waits for an event that the code under test makes well within it, such as a timer
    // armed or the end of a run once the fake clock passed its run limit: reached only when the event never comes, so
    // that the test fails instead of hanging.
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
}
