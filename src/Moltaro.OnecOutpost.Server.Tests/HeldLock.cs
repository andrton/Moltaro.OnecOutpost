using Npgsql;

namespace Moltaro.OnecOutpost.Server.Tests;

// A lock that a test holds in PostgreSQL to drive a race to a known order. The test takes the lock in a transaction of
// its own, starts the operations that contend for it, waits until every one of them is seen waiting for it, directly or
// behind another of them, and only then releases it. The release rolls the transaction back, so the lock changed
// nothing that the operations find. A test whose lock stands for a change that commits first commits it instead, and
// the operations find that change. An operation that skips locked rows never waits for the lock: the test then reads
// its result while the lock is held.
internal sealed class HeldLock : IAsyncDisposable
{
    // The sessions that wait for the holder: those it blocks, and those that they block in turn, as the second of two
    // sessions that want one row waits behind the first.
    private const string CountWaitingSql = """
        WITH RECURSIVE waiting (pid) AS (
            SELECT pid FROM pg_stat_activity
            WHERE @holder = ANY (pg_blocking_pids(pid))
            UNION
            SELECT activity.pid FROM pg_stat_activity activity
            JOIN waiting ON waiting.pid = ANY (pg_blocking_pids(activity.pid))
        )
        SELECT count(*) FROM waiting
        """;

    // Reached only when an operation never waits for the lock, or never ends without waiting: any machine starts and
    // ends the operations well within it.
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    private readonly string _connectionString;

    private readonly NpgsqlConnection _holder;

    private readonly NpgsqlTransaction _transaction;

    private HeldLock(string connectionString, NpgsqlConnection holder, NpgsqlTransaction transaction)
    {
        _connectionString = connectionString;
        _holder = holder;
        _transaction = transaction;
    }

    // Runs the statement that takes the lock, in a transaction that stays open until the lock is released.
    public static async Task<HeldLock> TakeAsync(
        string connectionString,
        string sql,
        CancellationToken cancellationToken)
    {
        NpgsqlConnection holder = new(connectionString);
        await holder.OpenAsync(cancellationToken);

        NpgsqlTransaction transaction = await holder.BeginTransactionAsync(cancellationToken);

        await using (NpgsqlCommand command = new(sql, holder, transaction))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new HeldLock(connectionString, holder, transaction);
    }

    // Completes once as many sessions wait for the lock as there are operations. An operation that ends before it is
    // seen waiting fails the test, with its own exception when it has one; so does a wait that is not seen within the
    // deadline.
    public Task WaitForAsync(IReadOnlyCollection<Task> operations, CancellationToken cancellationToken)
    {
        async Task<bool> AllWaitAsync(long waiting)
        {
            if (waiting >= operations.Count)
            {
                return true;
            }

            Task? ended = operations.FirstOrDefault(operation => operation.IsCompleted);

            if (ended is not null)
            {
                await ended;
                Assert.Fail("An operation ended before it was seen waiting for the lock.");
            }

            return false;
        }

        return ObserveAsync(
            AllWaitAsync,
            $"{operations.Count} operations were not seen waiting for the lock within {Deadline}.",
            cancellationToken);
    }

    // Gives the result of an operation that must not wait for the lock, as a statement that skips locked rows never
    // does. The lock is held all the while, so an operation seen waiting for it would wait until the release: it fails
    // the test at once, and so does an operation that neither ends nor waits within the deadline.
    public async Task<T> WithoutWaitingAsync<T>(Task<T> operation, CancellationToken cancellationToken)
    {
        Task<bool> EndedAsync(long waiting)
        {
            if (waiting > 0)
            {
                Assert.Fail("The operation waited for the lock.");
            }

            return Task.FromResult(operation.IsCompleted);
        }

        await ObserveAsync(
            EndedAsync,
            $"The operation neither ended nor waited for the lock within {Deadline}.",
            cancellationToken);

        return await operation;
    }

    // Commits the transaction, which lets the waiting operations go on and find its change, as they would find the
    // change of an operation that committed first.
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await _transaction.CommitAsync(cancellationToken);
    }

    // Rolls the transaction back, which lets the waiting operations go on.
    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        await _transaction.RollbackAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _holder.DisposeAsync();
    }

    // Reads the number of sessions that wait for the lock until `observed` says the state awaited is seen. PostgreSQL
    // gives no notice when a session begins to wait, so this reads pg_stat_activity, on a connection of its own and
    // outside any transaction: inside one, PostgreSQL would show the same picture of the sessions at every read. Each
    // read is one round trip and nothing sleeps between them, so the wait ends on the state observed, not after a time.
    private async Task ObserveAsync(
        Func<long, Task<bool>> observed,
        string notObserved,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Deadline);

        await using NpgsqlConnection observer = new(_connectionString);
        await observer.OpenAsync(deadline.Token);

        await using NpgsqlCommand countWaiting = new(CountWaitingSql, observer);
        countWaiting.Parameters.AddWithValue("holder", _holder.ProcessID);

        try
        {
            while (true)
            {
                long waiting = (long)(await countWaiting.ExecuteScalarAsync(deadline.Token))!;

                if (await observed(waiting))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Assert.Fail(notObserved);
        }
    }
}
