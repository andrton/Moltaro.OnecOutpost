using Moltaro.OnecOutpost.Server;

namespace Moltaro.OnecOutpost.TestSupport.Loopback;

// Two nodes of one server, as an application runs them: one store on one PostgreSQL, one signing key, the same options,
// and one fake clock, which both nodes arm their timers on. The Signals host is embedded in the first node as its
// ISignals, and the second reaches it through a SignalClient, so a set of either node wakes the waits of the other. A
// test puts a TestProxy in front, whose Node chooses the node of each request, and makes calls on either node.
//
// Either node can be stopped and started again. Stopping the first stops the Signals host with it, as when its process
// ends: the second's client loses its connection and tries again by itself. A node started again is a new
// LoopbackServer at a new port, which First or Second then gives; the first starts a new Signals host on the port of
// the old one, which the second's client reaches on its next attempt.
internal sealed class TwoNodes : IAsyncDisposable
{
    private readonly RemoteSignals _signals;

    private TwoNodes(RemoteSignals signals, LoopbackServer first, LoopbackServer second)
    {
        _signals = signals;
        First = first;
        Second = second;
    }

    // The node that embeds the Signals host.
    public LoopbackServer First { get; private set; }

    // The node that reaches the Signals host through a client.
    public LoopbackServer Second { get; private set; }

    // The clock of both nodes.
    public FakeClock Clock => First.Clock;

    // Starts both nodes on a schema of their own and completes once the second's client is connected to the host.
    public static async Task<TwoNodes> StartAsync(
        PostgreSqlFixture postgres,
        string schemaName,
        Action<OutpostServerOptions> configure,
        CancellationToken cancellationToken)
    {
        RemoteSignals signals = await RemoteSignals.StartAsync(cancellationToken);
        LoopbackServer? first = null;

        try
        {
            first = await LoopbackServer.StartAsync(postgres, schemaName, configure, signals.Host!, cancellationToken);
            LoopbackServer second = await first.StartAnotherAsync(signals.Client, cancellationToken);

            return new TwoNodes(signals, first, second);
        }
        catch
        {
            if (first is not null)
            {
                await first.DisposeAsync();
            }

            await signals.DisposeAsync();
            throw;
        }
    }

    // Stops the node as its host stops; the first, then the Signals host it embeds.
    public async Task StopAsync(LoopbackServer node, CancellationToken cancellationToken)
    {
        bool first = IsFirst(node);

        await node.StopAsync(cancellationToken);

        if (first)
        {
            await _signals.StopHostAsync();
        }
    }

    // Starts the node again, after stopping it if it runs, and gives the new node, which First or Second gives from
    // now on. The old one is disposed.
    public async Task<LoopbackServer> RestartAsync(LoopbackServer node, CancellationToken cancellationToken)
    {
        bool first = IsFirst(node);

        await node.DisposeAsync();

        if (!first)
        {
            Second = await node.StartAnotherAsync(_signals.Client, cancellationToken);

            return Second;
        }

        if (_signals.Host is not null)
        {
            await _signals.StopHostAsync();
        }

        _signals.StartHost();
        First = await node.StartAnotherAsync(_signals.Host!, cancellationToken);

        return First;
    }

    public async ValueTask DisposeAsync()
    {
        await Second.DisposeAsync();
        await First.DisposeAsync();
        await _signals.DisposeAsync();
    }

    // Whether the node is the first; a node that is neither is a mistake of the test.
    private bool IsFirst(LoopbackServer node)
    {
        if (node == First)
        {
            return true;
        }

        if (node == Second)
        {
            return false;
        }

        throw new ArgumentException("The node is neither of the two that run.", nameof(node));
    }
}
