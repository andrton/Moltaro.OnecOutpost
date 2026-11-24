using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// The one start of a worker's process, for the launcher and the console (the S10 brief, "Internal of S10c", review 3's
// entry 1, a fix as well): after CreateSuspended fails, one statement closes the caller's copies of the client ends of
// the pipes, those of the worker's input and output as that of the error end given, so that none waits for the
// finalizer; the error end, which the test can read, shows it, since a pipe reads to its end only once every copy of
// its client end is closed. A program that is missing fails the start with its Win32Exception, 2, ERROR_FILE_NOT_FOUND;
// its folder, the worker's working folder, exists, so that the error is not 267. When the job refuses a suspended
// worker, the process has exited before the failure is reported (the lab amendment to S10c).
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
[Collection(WindowStationCollection.Name)]
public sealed partial class WorkerProcessStartTests : IDisposable
{
    private const uint Signaled = 0;

    private readonly WorkerJob _job = new();

    public void Dispose()
    {
        _job.Dispose();
    }

    [Fact]
    public async Task A_start_of_a_program_that_is_missing_fails_with_the_Windows_error_2_and_the_error_end_given_reads_to_its_end_at_once()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using SafeAccessTokenHandle token = TestTokens.Own();
        using AnonymousPipeServerStream error = new(PipeDirection.In, HandleInheritability.Inheritable);
        string missing = Path.Combine(AppContext.BaseDirectory, $"missing-{Guid.NewGuid():N}.exe");

        Win32Exception exception = Assert.Throws<Win32Exception>(() => ComWorkerProcess.Start(
            WorkerCallEnum.AsUser,
            token,
            missing,
            "accounting",
            OutpostModeEnum.Read,
            "",
            _job,
            ownsJob: false,
            error));

        Assert.Equal(2, exception.NativeErrorCode);

        using StreamReader reader = new(error, Encoding.UTF8);

        Assert.Equal("", await reader.ReadToEndAsync(cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));
    }

    [Fact]
    public void A_worker_refused_by_its_job_has_exited_when_the_start_failure_is_reported()
    {
        using SafeAccessTokenHandle token = TestTokens.Own();
        using AnonymousPipeServerStream input = new(PipeDirection.Out, HandleInheritability.Inheritable);
        using AnonymousPipeServerStream output = new(PipeDirection.In, HandleInheritability.Inheritable);

        SafeProcessHandle process;
        nint thread;

        try
        {
            (process, thread) = WorkerCreation.CreateSuspended(
                WorkerCallEnum.AsUser,
                token,
                TestProgram.Path,
                $"\"{TestProgram.Path}\" accounting read",
                "",
                input.ClientSafePipeHandle,
                output.ClientSafePipeHandle,
                error: null);
        }
        finally
        {
            input.DisposeLocalCopyOfClientHandle();
            output.DisposeLocalCopyOfClientHandle();
        }

        using (process)
        {
            _job.Dispose();
            try
            {
                Assert.Throws<ObjectDisposedException>(() => WorkerCreation.JoinAndResume(_job, process, thread));
                Assert.Equal(Signaled, WaitForSingleObject(process, 0));
            }
            finally
            {
                // The test still ends its child if the behavior it checks regresses.
                WorkerCreation.Terminate(process);
                WaitForSingleObject(process, (uint)TestTimes.Deadline.TotalMilliseconds);
            }
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
}
