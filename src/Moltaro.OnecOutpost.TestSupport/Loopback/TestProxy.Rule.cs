namespace Moltaro.OnecOutpost.TestSupport.Loopback;

internal sealed partial class TestProxy
{
    // A loss that a test set: it takes the next request read after it that Match accepts, once. Met completes when it
    // took effect, with the request for DropRequest, the node's answer for DropAnswer and the HeldRequest for Hold.
    private sealed class Rule(LossEnum loss, Func<ProxiedRequest, bool> match)
    {
        public LossEnum Loss { get; } = loss;

        public Func<ProxiedRequest, bool> Match { get; } = match;

        public TaskCompletionSource<object> Met { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
