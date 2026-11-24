using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The job object of the COM workers, which ends them with the agent (principle 8): CreateJobObjectW with null
// attributes, so its handle is not inheritable, then SetInformationJobObject with JobObjectExtendedLimitInformation and
// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE, through LibraryImport, in a SafeHandle, no package (principle 12). A failure is a
// Win32Exception. The agent program makes one job and never disposes it, so Windows closes it when the agent ends,
// however it ends. A test disposes its own. Add may run on several tasks at once, as the system's calls may.
[SupportedOSPlatform("windows")]
internal sealed partial class WorkerJob : IDisposable
{
    // JobObjectExtendedLimitInformation of JOBOBJECTINFOCLASS.
    private const int ExtendedLimitInformationClass = 9;

    // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: closing the last handle of the job ends every process in it.
    private const uint KillOnJobClose = 0x2000;

    // JOB_OBJECT_LIMIT_BREAKAWAY_OK and JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK: a process of the job may leave it.
    private const uint BreakawayOk = 0x0800 | 0x1000;

    private readonly JobHandle _job;

    public WorkerJob()
    {
        _job = CreateJobObjectW(0, 0);

        if (_job.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();

            _job.Dispose();

            throw new Win32Exception(error);
        }

        ExtendedLimitInformation limits = new() { LimitFlags = KillOnJobClose };
        int length = Marshal.SizeOf<ExtendedLimitInformation>();

        if (!SetInformationJobObject(_job, ExtendedLimitInformationClass, in limits, length))
        {
            int error = Marshal.GetLastPInvokeError();

            _job.Dispose();

            throw new Win32Exception(error);
        }
    }

    // Puts the process into the job: AssignProcessToJobObject.
    public void Add(SafeProcessHandle process)
    {
        if (!AssignProcessToJobObject(_job, process))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    // Closes the job, which ends every process in it.
    public void Dispose()
    {
        _job.Dispose();
    }

    // How the calling process stands to jobs, for the reason of a failed start through the Secondary Logon service: in
    // no job, in a job that lets a process break away, the innermost one when jobs are nested, or in one that does not;
    // or that this cannot be told.
    public static string OfCaller()
    {
        if (!IsProcessInJob(GetCurrentProcess(), 0, out bool inJob))
        {
            return "whose job cannot be told";
        }

        if (!inJob)
        {
            return "in no job";
        }

        int length = Marshal.SizeOf<ExtendedLimitInformation>();

        if (!QueryInformationJobObject(0, ExtendedLimitInformationClass, out ExtendedLimitInformation limits, length, 0))
        {
            return "in a job whose limits cannot be read";
        }

        return (limits.LimitFlags & BreakawayOk) != 0
            ? "in a job that lets a process break away"
            : "in a job that lets no process break away";
    }

    // The security attributes and the name are null: a handle that no child inherits, of a job that has no name.
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial JobHandle CreateJobObjectW(nint attributes, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(
        JobHandle job,
        int informationClass,
        in ExtendedLimitInformation information,
        int length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(JobHandle job, SafeProcessHandle process);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsProcessInJob(nint process, nint job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    // A null job is the job of the calling process.
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(
        nint job,
        int informationClass,
        out ExtendedLimitInformation information,
        int length,
        nint returnLength);
}
