namespace Moltaro.OnecOutpost.TestSupport;

// The one reason with which a test of a behavior that is not implemented yet is skipped. Implementing the behavior
// removes the skip and makes the test pass with its existing assertions. A flaky test is never skipped
// with it.
public static class SkipReason
{
    public const string NotImplemented = "Not implemented yet: the sprint that implements this behavior removes the skip.";
}
