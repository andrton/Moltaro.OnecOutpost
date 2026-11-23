namespace Moltaro.OnecOutpost.TestSupport.Loopback;

internal sealed partial class TestProxy
{
    // What a rule of the proxy does with the request it takes.
    private enum LossEnum
    {
        // The request reaches no node, and the agent's connection is closed.
        DropRequest,

        // The request reaches the node, its answer reaches no one, and the agent's connection is closed.
        DropAnswer,

        // The request is kept until the test releases it.
        Hold,
    }
}
