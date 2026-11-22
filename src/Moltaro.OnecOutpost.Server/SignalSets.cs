using Moltaro.Signals;

namespace Moltaro.OnecOutpost.Server;

// The set of a key after the commit it announces, by the rule that the comment of OutpostServer states. Its result is
// ignored: a set that the Signals host did not confirm costs every waiter at most a deadline of its own.
internal static class SignalSets
{
    // Starts the set without a token, so that it goes out whatever happens to its waiter, and waits for it with the
    // waiter's token, which releases the waiter at once.
    public static async Task SetAfterCommitAsync(
        this ISignals signals,
        string key,
        CancellationToken cancellationToken)
    {
        Task<SignalSetResult> set = signals.SetAsync(key, CancellationToken.None).AsTask();

        try
        {
            await set.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The set goes on without its waiter. A failure that it still reports, such as the dispose of the Signals
            // client while it waits for the host, is observed here, so that it never becomes an unobserved exception.
            _ = set.ContinueWith(
                static finished => _ = finished.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            throw;
        }
    }
}
