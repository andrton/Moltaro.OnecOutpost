using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Moltaro.OnecOutpost.Agent.Tests.Connector;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// Principle 8 and concept §5: the agent ends its COM workers with itself. Every process that the launcher starts joins
// the job object, which ends every process in it when it closes (the S9b brief, "WorkerJob"); the agent program never
// disposes its job, so Windows closes it when the agent ends, however it ends. A test disposes its own.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class WorkerJobTests
{
    // The worker runs nothing and waits for its next command line, so only the job ends it.
    [Fact]
    public async Task Disposing_the_job_ends_a_running_worker()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TestConnector connector = TestConnector.Register(RegistryView.Registry64);
        using WorkerJob job = new();
        using ComWorkerLauncher launcher = new(TestProgram.Path, TestProgram.Path, connector.Finder, job);
        using IWorkerProcess worker = launcher.Start("accounting", OutpostModeEnum.Read);
        Assert.True(await worker.OpenAsync(TestLines.Connection(), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));
        (long Size, byte[] Outcome)? pid = await worker
            .RunAsync(TestLines.Command("pid"), cancellationToken)
            .WaitAsync(TestTimes.Deadline, cancellationToken);
        using Process process = Process.GetProcessById(TestLines.ResultOf(pid).GetInt32());

        job.Dispose();

        await process.WaitForExitAsync(cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
        Assert.True(process.HasExited);
    }
}
