using System.Diagnostics;
using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// A ComWorkerProcess over the test program that the test started itself, as the launcher starts a worker, in a job of
// the test, so that the test holds the process and sees it end. Disposing it disposes the worker, then the job, which
// ends the process if it still runs.
[SupportedOSPlatform("windows")]
internal sealed class StartedWorker : IDisposable
{
    private readonly WorkerJob _job;

    private StartedWorker(WorkerJob job, Process process)
    {
        _job = job;
        Process = process;
        Worker = new ComWorkerProcess(process.SafeHandle, process.StandardInput.BaseStream, process.StandardOutput.BaseStream);
    }

    public Process Process { get; }

    public ComWorkerProcess Worker { get; }

    // The program with these arguments, a base key and a mode unless a test gives a command line it refuses.
    public static StartedWorker Start(params string[] arguments)
    {
        WorkerJob job = new();

        try
        {
            return new StartedWorker(job, TestProgram.Start(job, arguments.Length == 0 ? ["accounting", "read"] : arguments));
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    // A started worker whose open gave true.
    public static async Task<StartedWorker> OpenAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        StartedWorker started = Start();

        try
        {
            Assert.True(await started.Worker.OpenAsync(TestLines.Connection(), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));

            return started;
        }
        catch
        {
            started.Dispose();
            throw;
        }
    }

    // Completes once the process ended; it fails the test when it does not end within the deadline of the tests.
    public Task EndedAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return Process.WaitForExitAsync(cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
    }

    public void Dispose()
    {
        Worker.Dispose();
        _job.Dispose();
    }
}
