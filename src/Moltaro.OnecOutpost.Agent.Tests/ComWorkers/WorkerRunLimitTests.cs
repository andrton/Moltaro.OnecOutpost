using System.Diagnostics;
using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Tests.Workers;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// Concept rule 9 and §5, through the pool on the real launcher with the test program (the S9b brief): a script that
// exceeds the run limit of its mode ends with its worker, whose process the pool kills; a Read fails with run-limit,
// and a Write is Unknown. The slot of the worker then waits a tenth of retryDelayMax, since the worker had opened, and
// starts a new process, which runs the next command. The test moves the fake clock only after it observed the run
// begin and the pool arm the timer it moves past.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class WorkerRunLimitTests
{
    [Theory]
    [InlineData("read", "failed")]
    [InlineData("write", "unknown")]
    public async Task A_script_past_the_run_limit_ends_its_worker_with_run_limit_and_after_the_slots_delay_a_new_worker_runs_the_next_command(
        string modeWord,
        string kindWord)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        OutpostModeEnum mode = TestCommands.ModeOf(modeWord);
        TimeSpan limit = TestPool.LimitOf(mode);
        await using ComWorkerPool test = await ComWorkerPool.OpenAsync();
        int first = await test.PidAsync(mode);
        using Process firstProcess = Process.GetProcessById(first);
        OutpostCommandMessage wait = test.Command(mode, "wait");
        DateTimeOffset start = test.Clock.GetUtcNow();

        Task<WorkerRun> waiting = test.Pool.RunAsync(wait, cancellationToken);
        await test.Launcher.RanAsync("accounting", mode, 2).WaitAsync(TestTimes.Deadline, cancellationToken);
        await test.Clock.WaitForTimerAsync(start + limit, cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
        test.Clock.Advance(limit);
        WorkerRun ended = await waiting.WaitAsync(TestTimes.Deadline, cancellationToken);
        test.Pool.Release(wait.Id);

        Assert.Equal(TestOutcomes.Own(TestOutcomes.KindOf(kindWord), OutpostOutcomeCodes.RunLimit), ended.Outcome);
        await firstProcess.WaitForExitAsync(cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);

        await test.PassExactlyAsync(ComWorkerPool.RetryDelayMax / 10);
        DateTimeOffset reopened = test.Clock.GetUtcNow();
        Assert.True(await test.Launcher.OpenedAsync("accounting", mode, 2).WaitAsync(TestTimes.Deadline, cancellationToken));
        await TestPool.WaitForOpenDeadlineEndedAsync(test.Clock, reopened + limit);
        Assert.Equal(1, test.Pool.Free("accounting", mode));
        int second = await test.PidAsync(mode);

        Assert.NotEqual(first, second);
    }
}
