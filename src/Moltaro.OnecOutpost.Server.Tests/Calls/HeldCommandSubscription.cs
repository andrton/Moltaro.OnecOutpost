using Moltaro.OnecOutpost.TestSupport.Loopback;

namespace Moltaro.OnecOutpost.Server.Tests.Calls;

// Subscribe is synchronous. Only the call being tested blocks here, for at most the deadline of the test.
internal sealed class HeldCommandSubscription(Func<string, bool> matches, CancellationToken stop)
    : IDisposable
{
    private readonly TaskCompletionSource<string> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool Matches(string key) => matches(key);

    public Task<string> WaitAsync(CancellationToken cancellationToken)
    {
        return _entered.Task.WaitAsync(LoopbackServer.Deadline, cancellationToken);
    }

    public void Block(string key)
    {
        _entered.SetResult(key);
        _released.Task.WaitAsync(LoopbackServer.Deadline, stop).GetAwaiter().GetResult();
    }

    public void Release() => _released.TrySetResult();

    public void Dispose() => Release();
}
