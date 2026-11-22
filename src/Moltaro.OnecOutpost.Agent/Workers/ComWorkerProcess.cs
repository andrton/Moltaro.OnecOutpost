using System.Diagnostics;
using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Agent.Workers;

// One COM worker process of the program's launcher, speaking S9's lines over its raw standard streams, as S9's lab
// helper does. A failed write gives false or null; cancellation kills and throws OperationCanceledException. The
// standard error is read and dropped, so a worker never waits on a full pipe. Limitation: the agent keeps no
// diagnostics of a worker; the console's check shows why one does not open (S10).
[SupportedOSPlatform("windows")]
internal sealed class ComWorkerProcess : IWorkerProcess
{
    // The process the launcher started, with its three standard streams redirected, already in the job.
    public ComWorkerProcess(Process process)
    {
        throw new NotSupportedException();
    }

    // Writes the connection line and reads one line of at most 1 KiB: true only for an object whose one member is
    // "ready":true; anything else, the end of the output included, kills the process and gives false.
    public Task<bool> OpenAsync(WorkerConnectionLine connection, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    // Writes the command line, ParametersJson as it is, and reads one line of at most OutcomeLimit plus 64 bytes. An
    // object with exactly size, an integer from 0, then outcome, any JSON value, read to
    // OutpostProtocol.MaxMessageDepth, gives size and the exact bytes of outcome. The end of the output before a whole
    // line, a longer line and any other line kill the process and give null.
    public Task<(long Size, byte[] Outcome)?> RunAsync(WorkerCommandLine command, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    // Process.Kill, an ended process ignored.
    public void Kill()
    {
        throw new NotSupportedException();
    }

    public void Dispose()
    {
        throw new NotSupportedException();
    }
}
