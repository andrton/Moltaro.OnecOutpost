namespace Moltaro.OnecOutpost.TestSupport.Loopback;

// A request of the agent that the TestProxy holds back: it has reached no node, and reaches one only when the test
// releases it. The agent's connection stays open meanwhile, until the agent closes it, as when its wait ends or it
// stops; a hold that is never released never reaches a node.
internal sealed class HeldRequest
{
    private readonly TestProxy _proxy;

    // The answer of the release, which the proxy also delivers to the agent while it still waits on the connection.
    private readonly TaskCompletionSource<AgentAnswer> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HeldRequest(TestProxy proxy, ProxiedRequest request)
    {
        _proxy = proxy;
        Request = request;
    }

    public ProxiedRequest Request { get; }

    // Completes with the answer of the first release.
    public Task<AgentAnswer> Answer => _answer.Task;

    // Sends the request to the node given, as the proxy forwards one, and gives its answer. The answer goes to the agent
    // too when it still waits on its connection; otherwise to the test alone. A second release sends it again, as a
    // replay, and reaches the test alone. An error of the connection to the node fails the release.
    public async Task<AgentAnswer> ReleaseAsync(LoopbackServer node, CancellationToken cancellationToken)
    {
        AgentAnswer answer = await _proxy.ReplayAsync(Request, node, cancellationToken);
        _answer.TrySetResult(answer);

        return answer;
    }
}
