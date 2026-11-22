using System.Diagnostics;
using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The job object of the COM workers, which ends them with the agent (principle 8): CreateJobObjectW with null
// attributes, so its handle is not inheritable, then SetInformationJobObject with JobObjectExtendedLimitInformation and
// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, through LibraryImport, in a SafeHandle, no package (principle 12). A failure is a
// Win32Exception. The agent program makes one job and never disposes it, so Windows closes it when the agent ends,
// however it ends. A test disposes its own.
[SupportedOSPlatform("windows")]
internal sealed partial class WorkerJob : IDisposable
{
    public WorkerJob()
    {
        throw new NotSupportedException();
    }

    // Puts the process into the job: AssignProcessToJobObject.
    public void Add(Process process)
    {
        throw new NotSupportedException();
    }

    // Closes the job, which ends every process in it.
    public void Dispose()
    {
        throw new NotSupportedException();
    }
}
