using System.Net;
using Moltaro.Signals;

namespace Moltaro.OnecOutpost.Server.Tests.Loopback;

// A Signals host over loopback TCP, apart from the node, and the SignalClient through which the node reaches it: the
// setup of an application whose Signals host runs in a process of its own. A test stops the host and starts another on
// the same port while the node runs, as when the host restarts. The client connects to the new host by itself and
// opens every subscription of the node on it again, and each wait on such a subscription then returns Joined. While no
// host runs, a set of the node gives Unavailable and is lost.
//
// The host and the client keep the clock of the system, so that heartbeats and attempts to reconnect go on by
// themselves while the fake clock of the node stands still; the waits of the node have no deadline on this clock.
internal sealed class RemoteSignals : IAsyncDisposable
{
    private const string Token = "test-token";

    private readonly IPEndPoint _endPoint;

    // The host that runs, or null while the test has stopped it.
    private SignalHost? _host;

    private RemoteSignals(SignalHost host, SignalClient client)
    {
        _host = host;
        _endPoint = host.LocalEndPoint!;
        Client = client;
    }

    // The ISignals of the node.
    public SignalClient Client { get; }

    // Starts a host on a free port of the loopback address and a client of it, and completes once the client is
    // connected, so that the node's first sets and subscriptions reach the host.
    public static async Task<RemoteSignals> StartAsync(CancellationToken cancellationToken)
    {
        SignalHost host = new(HostOptions(new IPEndPoint(IPAddress.Loopback, 0)));
        host.Start();

        SignalClient client = new(new SignalClientOptions { HostEndPoint = host.LocalEndPoint, Token = Token });
        RemoteSignals signals = new(host, client);

        try
        {
            Task connected = WhenConnectedAsync(client, cancellationToken);
            client.Start();
            await connected;
        }
        catch
        {
            await signals.DisposeAsync();
            throw;
        }

        return signals;
    }

    // Stops the host: the client loses its connection, and every subscription of the node with it.
    public async Task StopHostAsync()
    {
        SignalHost host = _host ?? throw new InvalidOperationException("No host runs.");
        _host = null;

        await host.DisposeAsync();
    }

    // Starts a new host on the port of the first, which the client reaches on its next attempt to connect.
    public void StartHost()
    {
        if (_host is not null)
        {
            throw new InvalidOperationException("A host runs already.");
        }

        _host = new SignalHost(HostOptions(_endPoint));
        _host.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();

        if (_host is not null)
        {
            await _host.DisposeAsync();
        }
    }

    private static SignalHostOptions HostOptions(IPEndPoint listenEndPoint)
    {
        return new SignalHostOptions { ListenEndPoint = listenEndPoint, Token = Token };
    }

    // Completes once the client is connected. The handler is attached before the state is read, so that no change is
    // missed; the wait has the deadline of every wait of a test.
    private static async Task WhenConnectedAsync(SignalClient client, CancellationToken cancellationToken)
    {
        TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnStateChanged(object? sender, SignalConnectionChangedEventArgs change)
        {
            if (change.State == SignalConnectionStateEnum.Connected)
            {
                connected.TrySetResult();
            }
        }

        client.StateChanged += OnStateChanged;

        try
        {
            if (client.State != SignalConnectionStateEnum.Connected)
            {
                await connected.Task.WaitAsync(LoopbackServer.Deadline, cancellationToken);
            }
        }
        finally
        {
            client.StateChanged -= OnStateChanged;
        }
    }
}
