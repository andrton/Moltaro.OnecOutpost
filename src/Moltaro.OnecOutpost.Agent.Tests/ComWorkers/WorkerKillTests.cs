using System.Runtime.Versioning;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// How a worker of the program ends at the agent's word (the boundary of S6b, the S9b brief, "ComWorkerProcess"): Kill
// is Process.Kill, an ended process ignored, and completes a pending run with null, as the pool's run limit relies on;
// a cancelled run kills the process and throws OperationCanceledException. The script wait writes nothing until a kill,
// so the run is pending until then.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class WorkerKillTests
{
    [Fact]
    public async Task Kill_ends_the_process_completes_a_pending_run_with_null_and_a_second_kill_is_ignored()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using StartedWorker started = await StartedWorker.OpenAsync();
        Task<(long Size, byte[] Outcome)?> run = started.Worker.RunAsync(TestLines.Command("wait"), cancellationToken);

        started.Worker.Kill();

        Assert.Null(await run.WaitAsync(TestTimes.Deadline, cancellationToken));
        await started.EndedAsync();
        started.Worker.Kill();
    }

    [Fact]
    public async Task A_cancelled_run_kills_the_process_and_throws_OperationCanceledException()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using StartedWorker started = await StartedWorker.OpenAsync();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<(long Size, byte[] Outcome)?> run = started.Worker.RunAsync(TestLines.Command("wait"), stop.Token);

        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TestTimes.Deadline, cancellationToken));
        await started.EndedAsync();
    }
}
