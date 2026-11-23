using Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Workers;

// The slots of the pool, which start their workers again (concept §5, the S9b brief, "Starting again"): Open gives each
// mode as many slots as it has workers, each holding one worker at a time, and every slot's first start happens before
// Open returns. A slot whose worker ended, its open gave false or a run dropped it, disposes the worker, waits a delay
// on the pool's clock and starts another: a tenth of retryDelayMax after a worker whose open gave true, and otherwise
// twice the delay before, from a tenth up to retryDelayMax. DisposeAsync cancels the delays first, so that nothing is
// started during or after it. The pool's policy has the default retryDelayMax of 30 seconds, so the delays are 3, 6,
// 12, 24, 30 and 30 seconds; the fake's opens that answer give their answer at once.
public sealed class WorkerRestartTests
{
    // Rule 8 and the S9b brief: the slots start their first workers at once, in the order of the policy, its bases and in
    // each its read workers before its write ones, even when no open answers.
    [Fact]
    public async Task Every_slot_starts_its_first_worker_before_Open_returns_in_the_order_of_the_policy()
    {
        await using TestPool test = TestPool.Create(readWorkers: 2, writeWorkers: 2);
        test.Launcher.Opens = (_, _, _) => FakeOpenEnum.Never;

        test.Pool.Open();

        Assert.Equal(
            ["accounting read 1", "accounting read 2", "accounting write 1", "accounting write 2", "warehouse read 1"],
            test.Launcher.Processes.Select(process => process.Name));
    }

    // Concept §5: a worker that does not open is started again with a growing delay, up to retryDelayMax; once one
    // opened, a worker that a run then ends comes back after a tenth again. Each delay is passed exactly: the slot's
    // timer is armed for it and for no less, and the slot starts its next worker once it passed.
    [Fact]
    public async Task A_slot_starts_its_worker_again_after_a_delay_that_doubles_up_to_retryDelayMax_and_is_a_tenth_after_a_worker_that_opened()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TestPool test = TestPool.Create();
        test.Launcher.Opens = (baseKey, mode, number) => (baseKey, mode) == ("accounting", OutpostModeEnum.Read) && number < 7
            ? FakeOpenEnum.NotReady
            : FakeOpenEnum.Ready;
        test.Launcher.Runs = _ => FakeRun.Ends();
        test.Pool.Open();
        int number = 1;

        foreach (int seconds in new[] { 3, 6, 12, 24, 30, 30 })
        {
            await test.PassExactlyAsync(TimeSpan.FromSeconds(seconds));
            number++;
            await test.Data.WaitForAsync(change => change.Line == $"start accounting read {number}");
        }

        DateTimeOffset reopened = test.Clock.GetUtcNow();
        await test.Data.WaitForAsync(change => change.Line == "open accounting read 7");
        await TestPool.WaitForOpenDeadlineEndedAsync(test.Clock, reopened + TestPool.ReadLimit);
        Assert.Equal(1, test.Pool.Free("accounting", OutpostModeEnum.Read));
        OutpostCommandMessage command = test.Command(OutpostModeEnum.Read);
        await test.Pool.RunAsync(command, cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
        test.Pool.Release(command.Id);

        await test.PassExactlyAsync(TimeSpan.FromSeconds(3));
        reopened = test.Clock.GetUtcNow();
        await test.Data.WaitForAsync(change => change.Line == "start accounting read 8");
        await test.Data.WaitForAsync(change => change.Line == "open accounting read 8");
        await TestPool.WaitForOpenDeadlineEndedAsync(test.Clock, reopened + TestPool.ReadLimit);
        Assert.Equal(1, test.Pool.Free("accounting", OutpostModeEnum.Read));

        Assert.True(test.Launcher.Process("accounting", OutpostModeEnum.Read, 7).IsDisposed);
    }

    // The S9b brief, "Lifetimes": DisposeAsync cancels the pool's token first, so that a slot waiting to start its
    // worker again starts nothing, however far the clock moves then. Every worker started is killed and disposed.
    [Fact]
    public async Task After_DisposeAsync_a_slot_that_waited_to_start_its_worker_again_starts_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TestPool test = TestPool.Create();
        test.Launcher.Opens = (baseKey, mode, _) => (baseKey, mode) == ("accounting", OutpostModeEnum.Read)
            ? FakeOpenEnum.NotReady
            : FakeOpenEnum.Ready;
        test.Pool.Open();
        await test.WaitForTimerAsync(test.Clock.GetUtcNow() + TimeSpan.FromSeconds(3));

        await test.DisposeAsync().AsTask().WaitAsync(TestTimes.Deadline, cancellationToken);
        test.Clock.Advance(test.Policy.RetryDelayMax);

        Assert.Equal(
            ["accounting read 1", "accounting write 1", "warehouse read 1"],
            test.Launcher.Processes.Select(process => process.Name));
        Assert.All(
            test.Launcher.Processes,
            process =>
            {
                Assert.True(process.IsKilled, $"{process.Name} is killed.");
                Assert.True(process.IsDisposed, $"{process.Name} is disposed.");
            });
    }

    // The S9b brief, "Lifetimes": a worker that a slot starts after DisposeAsync cancelled the pool is killed and
    // disposed at once, and never opened. The fake disposes the pool from the start of the slot's second worker, as
    // the host may dispose it while the task of a slot starts a worker.
    [Fact]
    public async Task A_worker_that_a_slot_starts_while_the_pool_is_disposed_is_killed_and_disposed_at_once_and_never_opened()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TestPool test = TestPool.Create();
        TaskCompletionSource<Task> disposing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        test.Launcher.Opens = (baseKey, mode, _) => (baseKey, mode) == ("accounting", OutpostModeEnum.Read)
            ? FakeOpenEnum.NotReady
            : FakeOpenEnum.Ready;
        test.Launcher.Started = process =>
        {
            if (process.Name == "accounting read 2")
            {
                disposing.SetResult(test.DisposeAsync().AsTask());
            }
        };
        test.Pool.Open();

        await test.PassExactlyAsync(TimeSpan.FromSeconds(3));
        Task disposed = await disposing.Task.WaitAsync(TestTimes.Deadline, cancellationToken);
        await disposed.WaitAsync(TestTimes.Deadline, cancellationToken);

        FakeWorkerProcess late = test.Launcher.Process("accounting", OutpostModeEnum.Read, 2);
        Assert.True(late.IsKilled);
        Assert.True(late.IsDisposed);
        Assert.Null(late.Connection);
        Assert.DoesNotContain("open accounting read 2", test.Data.LinesSince(0));
        Assert.Equal(4, test.Launcher.Processes.Count);
    }
}
