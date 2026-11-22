using Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Tests.Workers;

// The runs of the workers (concept rules 3, 8 and 9, the S6b brief). A command holds a free worker of its base and mode
// from RunAsync to Release; with none free, it is Failed base-unreachable at once, in either mode, as its script never
// started. A run races the run limit of its mode: when the limit wins, the worker is killed and the command ends with
// run-limit, Failed for a Read and Unknown for a Write. A worker that ends without an outcome gives connection-lost the
// same way. A worker is dropped after it ended, after a kill, and after it gave base-unreachable or connection-lost
// itself, and its mode then has one worker fewer until its slot starts it again, after a delay that no test here
// passes. DurationMs is the clock's time from the call to the outcome. Each test opens the workers and moves the clock
// past the deadlines of the opens first, so that the only timer a run waits on is its own.
public sealed class WorkerRunTests
{
    // Rule 8: a command holds its worker from its acceptance until its outcome is recorded, so that a poll never
    // declares a worker that a command still holds. The other modes keep theirs.
    [Fact]
    public async Task A_command_holds_its_worker_from_RunAsync_until_Release()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TestPool test = TestPool.Create();
        test.OpenAll();
        OutpostCommandMessage command = test.Command(OutpostModeEnum.Write);

        Task<WorkerRun> run = test.Pool.RunAsync(command, cancellationToken);

        Assert.Equal(0, test.Pool.Free("accounting", OutpostModeEnum.Write));

        await run.WaitAsync(TestTimes.Deadline, cancellationToken);

        Assert.Equal(0, test.Pool.Free("accounting", OutpostModeEnum.Write));
        Assert.Equal(1, test.Pool.Free("accounting", OutpostModeEnum.Read));

        test.Pool.Release(command.Id);

        Assert.Equal(1, test.Pool.Free("accounting", OutpostModeEnum.Write));
    }

    // Rule 8 and §8: no more commands run at once in a mode than it has workers. A command with no free worker never
    // started its script, so it is Failed base-unreachable at once, a Write too, and no worker is asked to run it.
    // Releasing it frees nothing, since it held no worker.
    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task Workers_bound_what_runs_and_a_command_without_a_free_worker_is_Failed_base_unreachable_at_once(
        string modeWord)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        OutpostModeEnum mode = TestCommands.ModeOf(modeWord);
        await using TestPool test = TestPool.Create(readWorkers: 2, writeWorkers: 2);
        test.Launcher.Runs = _ => FakeRun.Overruns();
        test.OpenAll();
        _ = test.Pool.RunAsync(test.Command(mode), cancellationToken);
        _ = test.Pool.RunAsync(test.Command(mode), cancellationToken);
        int count = test.Data.Changes.Count;
        OutpostCommandMessage third = test.Command(mode);

        Task<WorkerRun> run = test.Pool.RunAsync(third, cancellationToken);

        Assert.True(run.IsCompletedSuccessfully, "A command without a free worker ends at once.");
        Assert.Equal(TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Failed, "base-unreachable"), (await run).Outcome);
        Assert.Empty(test.Data.LinesSince(count));
        Assert.Single(test.Launcher.Process("accounting", mode, 1).Commands);
        Assert.Single(test.Launcher.Process("accounting", mode, 2).Commands);
        Assert.Equal(0, test.Pool.Free("accounting", mode));

        test.Pool.Release(third.Id);

        Assert.Equal(0, test.Pool.Free("accounting", mode));
    }

    // Rule 9: a script that exceeds the run limit of its mode ends with its worker: a Read fails with run-limit, and a
    // Write is Unknown. The worker is killed when the limit passes, the run is waited for, the duration is the limit,
    // and the worker is dropped.
    [Theory]
    [InlineData("read", "failed")]
    [InlineData("write", "unknown")]
    public async Task A_run_that_exceeds_the_run_limit_of_its_mode_is_killed_and_gives_run_limit(string modeWord, string kindWord)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        OutpostModeEnum mode = TestCommands.ModeOf(modeWord);
        TimeSpan limit = TestPool.LimitOf(mode);
        await using TestPool test = TestPool.Create();
        test.Launcher.Runs = _ => FakeRun.Overruns();
        test.OpenAll();
        FakeWorkerProcess worker = test.Launcher.Process("accounting", mode);
        OutpostCommandMessage command = test.Command(mode);
        DateTimeOffset start = test.Clock.GetUtcNow();

        Task<WorkerRun> run = test.Pool.RunAsync(command, cancellationToken);
        await test.WaitForTimerAsync(start + limit);
        test.Clock.Advance(limit - TestPool.Millisecond);

        Assert.False(worker.IsKilled);
        Assert.False(run.IsCompleted);

        test.Clock.Advance(TestPool.Millisecond);
        WorkerRun ended = await run.WaitAsync(TestTimes.Deadline, cancellationToken);

        Assert.True(worker.IsKilled);
        Assert.Equal(TestOutcomes.Own(TestOutcomes.KindOf(kindWord), "run-limit"), ended.Outcome);
        Assert.Equal((long)limit.TotalMilliseconds, ended.DurationMs);

        test.Pool.Release(command.Id);

        Assert.Equal(0, test.Pool.Free("accounting", mode));
    }

    // The acceptance of S6 and §8: a worker that ends without an outcome, or breaks its line, gives connection-lost:
    // Failed for a Read, and Unknown for a Write, whose script may have committed.
    [Theory]
    [InlineData("read", "failed")]
    [InlineData("write", "unknown")]
    public async Task A_worker_that_ends_without_an_outcome_gives_connection_lost(string modeWord, string kindWord)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TestPool test = TestPool.Create();
        test.Launcher.Runs = _ => FakeRun.Ends();
        test.OpenAll();

        WorkerRun run = await test.Pool
            .RunAsync(test.Command(TestCommands.ModeOf(modeWord)), cancellationToken)
            .WaitAsync(TestTimes.Deadline, cancellationToken);

        Assert.Equal(TestOutcomes.Own(TestOutcomes.KindOf(kindWord), "connection-lost"), run.Outcome);
    }

    // The S6b brief: a worker is dropped after it ended without an outcome, and after it gave base-unreachable or
    // connection-lost itself, after which S9's worker ends. Its mode has one worker fewer until its slot starts it
    // again (the S9b brief), after a delay that the clock does not pass here.
    [Theory]
    [InlineData("ends without an outcome")]
    [InlineData("gives Failed base-unreachable itself")]
    [InlineData("gives Failed connection-lost itself")]
    public async Task A_worker_that_ended_is_dropped_and_its_mode_has_one_worker_fewer_until_its_slot_starts_it_again(string form)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TestPool test = TestPool.Create(readWorkers: 2, writeWorkers: 1);
        FakeRun given = form switch
        {
            "ends without an outcome" => FakeRun.Ends(),
            "gives Failed base-unreachable itself" => FakeRun.Gives(
                TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Failed, "base-unreachable")),
            _ => FakeRun.Gives(TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Failed, "connection-lost")),
        };
        test.Launcher.Runs = _ => given;
        test.OpenAll();
        OutpostCommandMessage command = test.Command(OutpostModeEnum.Read);

        await test.Pool.RunAsync(command, cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
        test.Pool.Release(command.Id);

        Assert.Equal(1, test.Pool.Free("accounting", OutpostModeEnum.Read));
    }

    // The S6b brief and rule 3: a cancelled run kills its worker, waits for its run and throws, recording nothing, so
    // that the next start finds the command received without an outcome.
    [Fact]
    public async Task A_cancelled_run_kills_its_worker_and_throws()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TestPool test = TestPool.Create();
        test.Launcher.Runs = _ => FakeRun.Overruns();
        test.OpenAll();
        FakeWorkerProcess worker = test.Launcher.Process("accounting", OutpostModeEnum.Write);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<WorkerRun> run = test.Pool.RunAsync(test.Command(OutpostModeEnum.Write), stop.Token);
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimes.Deadline, cancellationToken));
        Assert.True(worker.IsKilled);
    }

    // The lines of S9 (the S6b brief): the command line of a run holds the command's script, the text of its parameters
    // as it came, its caller key, null for a Read without one, and its outcome limit.
    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task A_run_gives_its_worker_the_command_line_of_its_command(string modeWord)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        OutpostModeEnum mode = TestCommands.ModeOf(modeWord);
        const string Script = "Result = Parameters[\"rows\"];";
        const string Parameters = """{ "city": "Zürich", "rows": [1, 2] }""";
        await using TestPool test = TestPool.Create();
        test.OpenAll();
        OutpostCommandMessage issued = TestCommands.Of(Guid.NewGuid(), mode, test.Clock.GetUtcNow(), Script, Parameters);
        OutpostCommandMessage command = issued with { OutcomeLimit = 65_536 };

        await test.Pool.RunAsync(command, cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);

        WorkerCommandLine line = Assert.Single(test.Launcher.Process("accounting", mode).Commands);
        Assert.Equal(new WorkerCommandLine(Script, Parameters, command.CallerKey, 65_536), line);
        Assert.Equal(mode == OutpostModeEnum.Write ? "invoice-42" : null, line.CallerKey);
    }
}
