using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Tests.FakeFiles;
using Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Workers;

// A WorkerPool over the fake workers, with the fake clock at TestTimes.Start and the operation list of a data folder in
// memory. Its policy, unless a test gives another, holds accounting on a cluster, read and written, and warehouse in a
// folder, only read; a Read has a run limit of one minute and a Write one of two, so that a test tells the run limit
// of one mode from the other's. The fake's opens that answer give their answer at once, so those opens have ended when
// Open returns.
internal sealed class TestPool : IAsyncDisposable
{
    public const string Executor = @"C:\Program Files\Moltaro Outpost\OutpostExecutor.epf";

    public static readonly TimeSpan ReadLimit = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan WriteLimit = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    private TestPool(AgentPolicy policy, AgentSecrets secrets)
    {
        Policy = policy;
        Launcher = new FakeWorkerLauncher(Data);
        Pool = new WorkerPool(policy, secrets, Launcher, Executor, Clock);
    }

    public AgentPolicy Policy { get; }

    public FakeClock Clock { get; } = new(TestTimes.Start);

    public MemoryDataFolder Data { get; } = new();

    public FakeWorkerLauncher Launcher { get; }

    public WorkerPool Pool { get; }

    // The passwords of the default policy, as the console's secret writes them.
    public static AgentSecrets Secrets => new(
        [],
        new Dictionary<(string Base, OutpostModeEnum Mode), string>
        {
            [("accounting", OutpostModeEnum.Read)] = "read-pw",
            [("accounting", OutpostModeEnum.Write)] = "write-pw",
            [("warehouse", OutpostModeEnum.Read)] = "",
        });

    // The pool of the default policy, with one worker in each mode of accounting unless a test gives more.
    public static TestPool Create(int readWorkers = 1, int writeWorkers = 1)
    {
        return Create(PolicyOf(readWorkers, writeWorkers), Secrets);
    }

    public static TestPool Create(AgentPolicy policy, AgentSecrets secrets)
    {
        return new TestPool(policy, secrets);
    }

    // The default policy, with these workers for the modes of accounting and one for warehouse.
    public static AgentPolicy PolicyOf(int readWorkers, int writeWorkers)
    {
        return new AgentPolicy(
            90,
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(5),
            [
                new AgentBasePolicy(
                    "accounting",
                    "srv-1c:1541",
                    "accounting",
                    null,
                    new AgentModePolicy("outpost-read", readWorkers, ReadLimit, SafeMode: true),
                    new AgentModePolicy("outpost-write", writeWorkers, WriteLimit, SafeMode: true)),
                new AgentBasePolicy(
                    "warehouse",
                    null,
                    null,
                    @"D:\1C\Warehouse",
                    new AgentModePolicy("outpost-read", 1, ReadLimit, SafeMode: true),
                    null),
            ]);
    }

    // The run limit of a mode of accounting.
    public static TimeSpan LimitOf(OutpostModeEnum mode)
    {
        return mode == OutpostModeEnum.Read ? ReadLimit : WriteLimit;
    }

    // Opens the workers, and then moves the clock past the deadline of every open, the longest run limit of the policy,
    // so that the only timer a run waits on is its own.
    public void OpenAll()
    {
        Pool.Open(TestContext.Current.CancellationToken);

        IEnumerable<AgentModePolicy> modes = Policy.Bases
            .SelectMany(served => served.Write is null ? [served.Read] : new[] { served.Read, served.Write });

        Clock.Advance(modes.Max(mode => mode.RunLimit));
    }

    // A command of the agent for accounting, issued now; a Write with a caller key.
    public OutpostCommandMessage Command(OutpostModeEnum mode)
    {
        return TestCommands.Of(Guid.NewGuid(), mode, Clock.GetUtcNow());
    }

    // Completes once the pool has armed a timer due at or before dueBy, such as the run limit of a run it started.
    public Task WaitForTimerAsync(DateTimeOffset dueBy)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return Clock.WaitForTimerAsync(dueBy, cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
    }

    // Waits until the pool has armed a timer due by the delay from now, states that none is due a millisecond earlier,
    // and moves the clock by the delay.
    public async Task PassExactlyAsync(TimeSpan delay)
    {
        DateTimeOffset now = Clock.GetUtcNow();

        await WaitForTimerAsync(now + delay);
        Assert.False(Clock.HasTimerDueBy(now + delay - Millisecond), $"The pool waits less than {delay}.");

        Clock.Advance(delay);
    }

    // Completes once the deadline of an open has ended: the timer that the pool armed on the clock to fire at dueAt,
    // the time the open began plus the run limit of its mode. A slot publishes a worker whose open gave true before it
    // ends that timer (the S9b brief, "Threads"), so Free counts the worker from then on. It fails the test when the
    // timer does not end within the deadline of the tests.
    public static Task WaitForOpenDeadlineEndedAsync(FakeClock clock, DateTimeOffset dueAt)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return clock.WaitForTimerEndedAsync(dueAt, cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return Pool.DisposeAsync();
    }
}
