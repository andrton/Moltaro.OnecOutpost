using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// The creation of a worker with three ends:
// WorkerCreation.CreateSuspended with AsUser and the account's own token gives the worker the ends given as its
// standard input, output and error, the third one being the standard error of a worker that the console's check
// starts, and nothing else of the caller's; JoinAndResume puts it into the job and resumes it. What the test program
// writes to its standard error reaches the caller's end of the third pipe, which ends when the worker ends at the end of
// its input.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
[Collection(WindowStationCollection.Name)]
public sealed class WorkerErrorEndTests : IDisposable
{
    private const string Diagnostics = "the diagnostics of the test program";

    private readonly WorkerJob _job = new();

    public void Dispose()
    {
        _job.Dispose();
    }

    [Fact]
    public async Task A_worker_created_with_three_ends_writes_its_standard_error_to_the_third()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using SafeAccessTokenHandle token = TestTokens.Own();
        AnonymousPipeServerStream input = new(PipeDirection.Out, HandleInheritability.Inheritable);
        AnonymousPipeServerStream output = new(PipeDirection.In, HandleInheritability.Inheritable);
        using AnonymousPipeServerStream error = new(PipeDirection.In, HandleInheritability.Inheritable);

        (SafeProcessHandle process, nint thread) = WorkerCreation.CreateSuspended(
            WorkerCallEnum.AsUser,
            token,
            TestProgram.Path,
            $"\"{TestProgram.Path}\" accounting read",
            "",
            input.ClientSafePipeHandle,
            output.ClientSafePipeHandle,
            error.ClientSafePipeHandle);

        input.DisposeLocalCopyOfClientHandle();
        output.DisposeLocalCopyOfClientHandle();
        error.DisposeLocalCopyOfClientHandle();
        WorkerCreation.JoinAndResume(_job, process, thread);

        // The dispose closes the worker's input, at whose end it ends, and with it the third pipe.
        using (ComWorkerProcess worker = new(process, input, output))
        {
            Assert.True(await worker.OpenAsync(TestLines.Connection(), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));

            (long Size, byte[] Outcome)? given = await worker
                .RunAsync(TestLines.Command($"diagnostics {Diagnostics}"), cancellationToken)
                .WaitAsync(TestTimes.Deadline, cancellationToken);

            Assert.Equal(JsonValueKind.Null, TestLines.ResultOf(given).ValueKind);
        }

        using StreamReader reader = new(error, Encoding.UTF8);

        Assert.Equal(Diagnostics + "\n", await reader.ReadToEndAsync(cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));
    }
}
