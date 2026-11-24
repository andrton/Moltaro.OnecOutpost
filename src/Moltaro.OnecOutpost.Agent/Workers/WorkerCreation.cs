using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The creation of a worker's process, suspended, and its start inside a job, for the agent's launcher and for the
// console's check. Every call to Windows goes through LibraryImport, no package (principle 12).
[SupportedOSPlatform("windows")]
internal static partial class WorkerCreation
{
    // CREATE_SUSPENDED, CREATE_UNICODE_ENVIRONMENT, EXTENDED_STARTUPINFO_PRESENT and CREATE_NO_WINDOW.
    private const uint CreationFlags = 0x00000004 | 0x00000400 | 0x00080000 | 0x08000000;

    // STARTF_USESTDHANDLES.
    private const int UseStandardHandles = 0x00000100;

    // PROC_THREAD_ATTRIBUTE_HANDLE_LIST: the only handles that the process inherits.
    private const nuint HandleListAttribute = 0x00020002;

    // What ResumeThread gives when it fails, (DWORD)-1.
    private const uint ResumeFailed = uint.MaxValue;

    // The exit code of a worker that never resumed, as Process.Kill gives it.
    private const uint KilledExitCode = uint.MaxValue;

    // Creates the process of the program under the token, suspended and without a window, with the command line
    // "{program}" {base key} {read|write} as given, the environment of the token's account, CreateEnvironmentBlock(token,
    // false), the program's folder as its working folder, lpDesktop as given, "" connecting it to the window station of
    // the caller's logon session, and the client ends given as its standard input, output and error, a null error when
    // none is given. AsUser is CreateProcessAsUserW with a PROC_THREAD_ATTRIBUTE_HANDLE_LIST of exactly those ends, so
    // that the process inherits nothing else; WithToken is CreateProcessWithTokenW through the Secondary Logon service,
    // which gives the process duplicates of its standard handles alone. A failed call is a Win32Exception with its code,
    // and then no process exists. The caller owns the handles it is given and those it gets.
    public static (SafeProcessHandle Process, nint Thread) CreateSuspended(
        WorkerCallEnum call,
        SafeAccessTokenHandle token,
        string program,
        string commandLine,
        string desktop,
        SafePipeHandle input,
        SafePipeHandle output,
        SafePipeHandle? error)
    {
        if (call == WorkerCallEnum.WithToken)
        {
            throw new NotSupportedException("CreateProcessWithTokenW through the Secondary Logon service is S10b's implementation.");
        }

        return AsUser(token, program, commandLine, desktop, input, output, error);
    }

    // Puts the suspended process into the job and resumes its thread, whose handle it closes, so that a worker is in the
    // job before any of its code runs. A process that the job does not take, which would outlive its owner, or whose
    // thread does not resume is terminated and its Win32Exception passes; any other exception, such as the
    // ObjectDisposedException of a disposed job, passes after the termination as well, so that no process is ever left
    // outside the job.
    public static void JoinAndResume(WorkerJob job, SafeProcessHandle process, nint thread)
    {
        bool resumed = false;

        try
        {
            job.Add(process);

            if (ResumeThread(thread) == ResumeFailed)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            resumed = true;
        }
        finally
        {
            CloseHandle(thread);

            if (!resumed)
            {
                // The handle of a created process may terminate it, so the call fails only for one that has ended.
                TerminateProcess(process, KilledExitCode);
            }
        }
    }

    private static unsafe (SafeProcessHandle Process, nint Thread) AsUser(
        SafeAccessTokenHandle token,
        string program,
        string commandLine,
        string desktop,
        SafePipeHandle input,
        SafePipeHandle output,
        SafePipeHandle? error)
    {
        if (!CreateEnvironmentBlock(out nint environment, token, inherit: false))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        // The first call gives the size of a list of one attribute and fails, with ERROR_INSUFFICIENT_BUFFER.
        nuint listSize = 0;
        InitializeProcThreadAttributeList(0, 1, 0, ref listSize);

        nint attributes = Marshal.AllocHGlobal((nint)listSize);
        bool initialized = false;

        try
        {
            initialized = InitializeProcThreadAttributeList(attributes, 1, 0, ref listSize);

            if (!initialized)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            // The list points at the handles, which stay on this stack until CreateProcessAsUserW returned.
            int count = error is null ? 2 : 3;
            nint* inherited = stackalloc nint[3];
            inherited[0] = input.DangerousGetHandle();
            inherited[1] = output.DangerousGetHandle();
            inherited[2] = error?.DangerousGetHandle() ?? 0;

            if (!UpdateProcThreadAttribute(attributes, 0, HandleListAttribute, (nint)inherited, (nuint)(count * sizeof(nint)), 0, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            // CreateProcessAsUserW may write into the command line, so it gets a copy of its own, ended by a null.
            char[] line = [.. commandLine, '\0'];

            fixed (char* desktopName = desktop)
            fixed (char* lineStart = line)
            {
                StartupInfo startup = new()
                {
                    Size = Marshal.SizeOf<StartupInfo>(),
                    Desktop = (nint)desktopName,
                    Flags = UseStandardHandles,
                    StandardInput = inherited[0],
                    StandardOutput = inherited[1],
                    StandardError = inherited[2],
                    AttributeList = attributes,
                };

                if (!CreateProcessAsUserW(
                    token,
                    program,
                    lineStart,
                    0,
                    0,
                    inheritHandles: true,
                    CreationFlags,
                    environment,
                    Path.GetDirectoryName(program)!,
                    in startup,
                    out ProcessInformation created))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                return (new SafeProcessHandle(created.Process, ownsHandle: true), created.Thread);
            }
        }
        finally
        {
            if (initialized)
            {
                DeleteProcThreadAttributeList(attributes);
            }

            Marshal.FreeHGlobal(attributes);
            DestroyEnvironmentBlock(environment);
        }
    }

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateEnvironmentBlock(
        out nint environment,
        SafeAccessTokenHandle token,
        [MarshalAs(UnmanagedType.Bool)] bool inherit);

    [LibraryImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyEnvironmentBlock(nint environment);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(nint attributeList, int attributeCount, int flags, ref nuint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(
        nint attributeList,
        uint flags,
        nuint attribute,
        nint value,
        nuint size,
        nint previousValue,
        nint returnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(nint attributeList);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CreateProcessAsUserW(
        SafeAccessTokenHandle token,
        string applicationName,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string currentDirectory,
        in StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint ResumeThread(nint thread);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeProcessHandle process, uint exitCode);
}
