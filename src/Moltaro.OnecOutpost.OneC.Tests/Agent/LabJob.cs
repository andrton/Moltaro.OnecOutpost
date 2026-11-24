using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.OneC.Tests.Lab;

namespace Moltaro.OnecOutpost.OneC.Tests.Agent;

// A job of the lab's tests that the process of the tests joins once, before any agent of a test starts a worker. It
// sets no limit. Every process that the tests start, and every process that those start, is in it as well, whichever
// account it runs under and in whatever job of its own the agent puts it, since jobs nest: so the tests find the
// processes of an agent's workers, which the agent does not name. A process leaves the job as it begins to end, before
// Windows has unmapped the files of its program, so the end that a removal of the program needs is that of the process
// itself: a handle of it that is signaled, then closed. The tests open each process with the right to wait for it,
// under their own account, or, for a worker under a worker account, which only that account may open, under that
// account, whose logon they may impersonate since they log it on with its password. A completion port gets a message
// of the job for each process that leaves it, so that a wait for the processes to leave the job looks at the job again
// at each message, up to its deadline. The job and the port live as long as the process of the tests.
[SupportedOSPlatform("windows")]
internal static partial class LabJob
{
    // SYNCHRONIZE: the right to wait for a process.
    private const uint Synchronize = 0x00100000;

    // ERROR_ACCESS_DENIED, of a process that only another account may open.
    private const int AccessDenied = 5;

    // JobObjectBasicProcessIdList and JobObjectAssociateCompletionPortInformation of JOBOBJECTINFOCLASS, and the error
    // of a list too long for its buffer.
    private const int BasicProcessIdListClass = 3;

    private const int AssociateCompletionPortClass = 7;

    private const int ErrorMoreData = 234;

    // WAIT_TIMEOUT, the error of GetQueuedCompletionStatus at the end of its time, and WAIT_OBJECT_0.
    private const int WaitTimeout = 258;

    private const uint Signaled = 0;

    // INVALID_HANDLE_VALUE, which CreateIoCompletionPort takes for a port of no file.
    private const nint NoFile = -1;

    private static readonly Lazy<(nint Job, nint Port)> Joined = new(Join);

    // Joins the job once; later calls do nothing.
    public static void EnsureJoined()
    {
        _ = Joined.Value;
    }

    // The process ids of the job but that of the tests: the processes that the tests started, and theirs, that run.
    public static IReadOnlyList<long> Others()
    {
        return [.. Ids(Joined.Value.Job).Where(id => id != Environment.ProcessId)];
    }

    // A handle of each of these processes that still runs, with the right to wait for it: opened under the account of
    // the tests, or under the worker account for one that only that account may open. A process that ended already, or
    // that neither account may open, has none; the job's wait covers it.
    public static List<SafeProcessHandle> Open(IReadOnlyList<long> processes, LabAccount? workerAccount)
    {
        List<SafeProcessHandle> handles = [];
        List<long> denied = [];

        foreach (long id in processes)
        {
            SafeProcessHandle handle = OpenProcess(Synchronize, inherit: false, (uint)id);
            int error = Marshal.GetLastPInvokeError();

            if (!handle.IsInvalid)
            {
                handles.Add(handle);

                continue;
            }

            handle.Dispose();

            if (error == AccessDenied)
            {
                denied.Add(id);
            }
        }

        if (denied.Count == 0 || workerAccount is null)
        {
            return handles;
        }

        using SafeAccessTokenHandle? token = WorkerLogon.LogOn(workerAccount.User, workerAccount.Password, out _);

        if (token is null)
        {
            return handles;
        }

        WindowsIdentity.RunImpersonated(token, () =>
        {
            foreach (long id in denied)
            {
                SafeProcessHandle handle = OpenProcess(Synchronize, inherit: false, (uint)id);

                if (handle.IsInvalid)
                {
                    handle.Dispose();
                }
                else
                {
                    handles.Add(handle);
                }
            }
        });

        return handles;
    }

    // Waits until none of these processes is in the job and every handle is signaled: true once they all ended, false
    // when the deadline came first.
    public static bool WaitUntilEnded(IReadOnlyCollection<long> processes, IReadOnlyList<SafeProcessHandle> handles, TimeSpan deadline)
    {
        (nint job, nint port) = Joined.Value;
        long end = Environment.TickCount64 + (long)deadline.TotalMilliseconds;

        while (Ids(job).Intersect(processes).Any())
        {
            long left = end - Environment.TickCount64;

            if (left <= 0)
            {
                return false;
            }

            // A message of the job wakes the wait, such as the end of one of its processes; a message of any other
            // process only makes it look at the job again.
            if (!GetQueuedCompletionStatus(port, out _, out _, out _, (uint)left))
            {
                int error = Marshal.GetLastPInvokeError();

                if (error != WaitTimeout)
                {
                    throw new Win32Exception(error);
                }
            }
        }

        foreach (SafeProcessHandle handle in handles)
        {
            long left = Math.Max(0, end - Environment.TickCount64);

            if (WaitForSingleObject(handle, (uint)left) != Signaled)
            {
                return false;
            }
        }

        return true;
    }

    private static unsafe (nint Job, nint Port) Join()
    {
        nint job = CreateJobObjectW(0, 0);

        if (job == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        nint port = CreateIoCompletionPort(NoFile, 0, 0, 1);

        if (port == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        // JOBOBJECT_ASSOCIATE_COMPLETION_PORT: the completion key, unused, then the port.
        nint* association = stackalloc nint[2];
        association[0] = 0;
        association[1] = port;

        if (!SetInformationJobObject(job, AssociateCompletionPortClass, association, (uint)(2 * sizeof(nint))))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        if (!AssignProcessToJobObject(job, GetCurrentProcess()))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return (job, port);
    }

    // The process ids of the job and of the jobs nested in it, the agents' among them.
    private static unsafe List<long> Ids(nint job)
    {
        for (int capacity = 64; ; capacity *= 2)
        {
            // JOBOBJECT_BASIC_PROCESS_ID_LIST: NumberOfAssignedProcesses and NumberOfProcessIdsInList, two DWORDs, then
            // the ids as ULONG_PTR, from byte 8.
            byte[] list = new byte[(2 * sizeof(uint)) + (capacity * sizeof(nuint))];

            fixed (byte* start = list)
            {
                if (QueryInformationJobObject(job, BasicProcessIdListClass, start, (uint)list.Length, 0))
                {
                    uint count = *(uint*)(start + sizeof(uint));
                    nuint* ids = (nuint*)(start + (2 * sizeof(uint)));
                    List<long> given = [];

                    for (int index = 0; index < count; index++)
                    {
                        given.Add((long)ids[index]);
                    }

                    return given;
                }
            }

            int error = Marshal.GetLastPInvokeError();

            if (error != ErrorMoreData)
            {
                throw new Win32Exception(error);
            }
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateJobObjectW(nint attributes, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateIoCompletionPort(nint file, nint existingPort, nuint key, uint threads);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetInformationJobObject(nint job, int informationClass, void* information, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryInformationJobObject(
        nint job,
        int informationClass,
        void* information,
        uint length,
        nint returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint id);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetQueuedCompletionStatus(
        nint port,
        out uint bytes,
        out nuint key,
        out nint overlapped,
        uint milliseconds);
}
