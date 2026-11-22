namespace Moltaro.OnecOutpost.Server.Tests;

// The other half of a test that a member refuses arguments that break a rule: arguments within the rule pass its
// checks.
internal static class ArgumentChecks
{
    // A token that is cancelled already. A member called with it ends as cancelled and changes nothing once it has
    // passed the checks of its arguments, so such a test needs no store and no agent.
    public static CancellationToken Cancelled { get; } = new(canceled: true);

    // Asserts that the call does not refuse its arguments. What it does after the checks is not the subject: a call
    // with a cancelled token ends as cancelled.
    public static void AssertPassed(Action call)
    {
        Exception? exception = Record.Exception(call);

        Assert.False(exception is ArgumentException, $"The call refused its arguments: {exception?.Message}");
    }
}
