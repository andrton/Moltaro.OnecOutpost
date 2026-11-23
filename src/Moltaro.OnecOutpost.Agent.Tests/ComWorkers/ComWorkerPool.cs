using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Tests.Workers;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Workers;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// A WorkerPool on the real ComWorkerLauncher, as the agent program makes it, with the test program in place of the
// worker as both programs, a WorkerJob of the test and a class of the connector registered for the current user in the
// view of 64 bits; the pool's clock is a fake one at TestTimes.Start. Its policy holds accounting, read and written,
// one worker in each mode, with the run limits of TestPool and the default retryDelayMax of 30 seconds. The launcher is
// wrapped in TestSupport's ObservedLauncher, so that a test moves the clock, or asks for a free worker, only after it
// observed what it waits for. Disposing it disposes the pool, then the launcher, then the job, then the class.
[SupportedOSPlatform("windows")]
internal sealed class ComWorkerPool : IAsyncDisposable
{
    public static readonly TimeSpan RetryDelayMax = TimeSpan.FromSeconds(30);

    private readonly TestConnector _connector;

    private readonly WorkerJob _job;

    private readonly ComWorkerLauncher _launcher;

    private ComWorkerPool(TestConnector connector, WorkerJob job)
    {
        _connector = connector;
        _job = job;
        _launcher = new ComWorkerLauncher(TestProgram.Path, TestProgram.Path, connector.ProgId, job);
        Launcher = new ObservedLauncher(_launcher);

        AgentPolicy policy = new(
            90,
            null,
            RetryDelayMax,
            TimeSpan.FromMinutes(5),
            [
                new AgentBasePolicy(
                    "accounting",
                    "srv-1c:1541",
                    "accounting",
                    null,
                    new AgentModePolicy("outpost-read", 1, TestPool.ReadLimit, SafeMode: true, WorkerAccount: null),
                    new AgentModePolicy("outpost-write", 1, TestPool.WriteLimit, SafeMode: true, WorkerAccount: null)),
            ]);

        Pool = new WorkerPool(policy, TestPool.Secrets, Launcher, TestPool.Executor, Clock);
    }

    public FakeClock Clock { get; } = new(TestTimes.Start);

    public ObservedLauncher Launcher { get; }

    public WorkerPool Pool { get; }

    // Opens the workers, and completes once both opened and the deadlines of their opens, armed when the clock was at
    // TestTimes.Start, ended, so that Free counts them.
    public static async Task<ComWorkerPool> OpenAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TestConnector connector = TestConnector.Register(RegistryView.Registry64);
        ComWorkerPool test = new(connector, new WorkerJob());

        try
        {
            test.Pool.Open();

            foreach (OutpostModeEnum mode in new[] { OutpostModeEnum.Read, OutpostModeEnum.Write })
            {
                Assert.True(await test.Launcher.OpenedAsync("accounting", mode, 1).WaitAsync(TestTimes.Deadline, cancellationToken));
                await TestPool.WaitForOpenDeadlineEndedAsync(test.Clock, TestTimes.Start + TestPool.LimitOf(mode));
                Assert.Equal(1, test.Pool.Free("accounting", mode));
            }

            return test;
        }
        catch
        {
            await test.DisposeAsync();
            throw;
        }
    }

    // A command of the agent for accounting whose script the test program runs, issued now.
    public OutpostCommandMessage Command(OutpostModeEnum mode, string script)
    {
        return TestCommands.Of(Guid.NewGuid(), mode, Clock.GetUtcNow(), script);
    }

    // Runs pid on a free worker of the mode, frees the worker, and gives the identity of its process.
    public async Task<int> PidAsync(OutpostModeEnum mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        OutpostCommandMessage command = Command(mode, "pid");

        WorkerRun run = await Pool.RunAsync(command, cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
        Pool.Release(command.Id);

        JsonElement? result = OutpostCodec.ReadOutcome(mode, run.Outcome).Result;
        Assert.NotNull(result);

        return result.Value.GetInt32();
    }

    // Waits until the pool has armed a timer due by the delay from now, states that none is due a millisecond earlier,
    // and moves the clock by the delay.
    public async Task PassExactlyAsync(TimeSpan delay)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DateTimeOffset now = Clock.GetUtcNow();

        await Clock.WaitForTimerAsync(now + delay, cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
        Assert.False(Clock.HasTimerDueBy(now + delay - TestPool.Millisecond), $"The pool waits less than {delay}.");

        Clock.Advance(delay);
    }

    public async ValueTask DisposeAsync()
    {
        await Pool.DisposeAsync();
        _launcher.Dispose();
        _job.Dispose();
        _connector.Dispose();
    }
}
