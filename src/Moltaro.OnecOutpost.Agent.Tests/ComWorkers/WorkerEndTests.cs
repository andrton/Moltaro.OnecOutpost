using System.Diagnostics;
using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// A worker that ends during a run, as S9's worker ends with exit code 3 when its session ended while the script ran
// (the S9b brief, "ComWorkerProcess"; the S6b brief, "WorkerPool"): its output ends before a whole line, so the run
// gives null, and the pool gives connection-lost, Failed for a Read and Unknown for a Write, whose script may have
// committed. The script exit 3 of the test program ends it so, without an outcome.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class WorkerEndTests
{
    [Fact]
    public async Task The_end_of_the_output_during_a_run_gives_null_and_the_process_has_ended()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using StartedWorker started = await StartedWorker.OpenAsync();

        (long Size, byte[] Outcome)? given = await started.Worker
            .RunAsync(TestLines.Command("exit 3"), cancellationToken)
            .WaitAsync(TestTimes.Deadline, cancellationToken);

        Assert.Null(given);
        await started.EndedAsync();
        Assert.Equal(3, started.Process.ExitCode);
    }

    [Theory]
    [InlineData("read", "failed")]
    [InlineData("write", "unknown")]
    public async Task A_worker_that_ends_during_a_run_gives_connection_lost_as_its_mode_says(string modeWord, string kindWord)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        OutpostModeEnum mode = TestCommands.ModeOf(modeWord);
        await using ComWorkerPool test = await ComWorkerPool.OpenAsync();
        using Process process = Process.GetProcessById(await test.PidAsync(mode));
        OutpostCommandMessage command = test.Command(mode, "exit 3");

        WorkerRun run = await test.Pool.RunAsync(command, cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
        test.Pool.Release(command.Id);

        Assert.Equal(TestOutcomes.Own(TestOutcomes.KindOf(kindWord), OutpostOutcomeCodes.ConnectionLost), run.Outcome);
        await process.WaitForExitAsync(cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
    }
}
