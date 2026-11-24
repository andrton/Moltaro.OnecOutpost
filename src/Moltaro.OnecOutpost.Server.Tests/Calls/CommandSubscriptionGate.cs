using System.Net;
using Moltaro.Signals;

namespace Moltaro.OnecOutpost.Server.Tests.Calls;

// The call has its command ID when it subscribes to that key, and has yet to pass it in the store. Tests stop at
// that boundary while the real purge runs. All other subscriptions and sets go to the host as usual.
internal sealed class CommandSubscriptionGate : ISignals, IAsyncDisposable
{
    private readonly SignalHost _host = new(new SignalHostOptions
    {
        ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
        Token = "test-token",
    });

    private HeldCommandSubscription? _next;

    public SignalConnectionStateEnum State => _host.State;

    public event EventHandler<SignalConnectionChangedEventArgs>? StateChanged
    {
        add => _host.StateChanged += value;
        remove => _host.StateChanged -= value;
    }

    public HeldCommandSubscription HoldNext(Func<string, bool> matches, CancellationToken cancellationToken)
    {
        HeldCommandSubscription held = new(matches, cancellationToken);
        Assert.Null(Interlocked.CompareExchange(ref _next, held, null));

        return held;
    }

    public ValueTask<SignalSetResult> SetAsync(string key, CancellationToken cancellationToken)
    {
        return _host.SetAsync(key, cancellationToken);
    }

    public ISignalSubscription Subscribe(string key, string group)
    {
        HeldCommandSubscription? held = Volatile.Read(ref _next);

        if (held is not null && held.Matches(key) && Interlocked.CompareExchange(ref _next, null, held) == held)
        {
            held.Block(key);
        }

        return _host.Subscribe(key, group);
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
