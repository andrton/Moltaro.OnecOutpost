using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.OneC.Tests.Lab;

namespace Moltaro.OnecOutpost.OneC.Tests.Agent;

// A start of the worker program by the test itself under a worker account, for the message of a test whose worker did
// not open: the agent keeps no reason for a worker that does not start, and the launcher keeps no standard error of a
// worker, by design. It logs the account on for a batch job, as the launcher does, and starts the program as the
// launcher starts a worker of a logged-on base, with CreateProcessAsUserW, the environment of the account, the
// program's folder, no window and the desktop that the launcher made for the base (the S9c brief, "The desktop of a
// worker"), "{station}\OnecOutpost-{base key}" on the agent's own non-interactive station, which the system names
// Service-0x{high}-{low}$ after the agent's logon session; but with no command line and no standard streams, so that
// the worker ends at once with exit code 1, its exit for a command line it refuses. A desktop that the agent did not
// make, or that does not admit the logon, makes the program end with 0xC0000142 before its Main. What it says is the
// Win32 error of the logon or of the start, or the exit code of the program; never the password, which LabLogon gives
// only to LogonUserW.
[SupportedOSPlatform("windows")]
internal static partial class LabWorkerProbe
{
    // CREATE_UNICODE_ENVIRONMENT and CREATE_NO_WINDOW.
    private const uint CreationFlags = 0x00000400 | 0x08000000;

    // WAIT_OBJECT_0.
    private const uint Signaled = 0;

    // The exit code of the worker for a command line it refuses (WorkerExitCodes.BadInput of the worker).
    private const uint BadInput = 1;

    // TOKEN_QUERY, and TokenStatistics of TOKEN_INFORMATION_CLASS, whose AuthenticationId is at byte 8.
    private const uint TokenQuery = 0x0008;

    private const int TokenStatisticsClass = 10;

    private const int TokenStatisticsLength = 56;

    public static string Describe(LabAccount account, string program, string baseKey, TimeSpan deadline)
    {
        string desktop = $@"{AgentStation()}\OnecOutpost-{baseKey}";
        string about = $"a start of the worker program by the test under {account.User} on {desktop}, without a command line,";

        using SafeAccessTokenHandle? token = LabLogon.LogOn(account, out int logonError);

        if (token is null)
        {
            return $"{about} found that the account cannot log on: {Error(logonError)}";
        }

        return Start(about, token, program, desktop, deadline);
    }

    // The name of the non-interactive window station of the logon session of the agent, which runs in the process of
    // the tests: Service-0x{high}-{low}$ of the session's identifier, in hexadecimal.
    private static unsafe string AgentStation()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out SafeAccessTokenHandle token))
        {
            token.Dispose();

            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        using (token)
        {
            byte* statistics = stackalloc byte[TokenStatisticsLength];

            if (!GetTokenInformation(token, TokenStatisticsClass, statistics, TokenStatisticsLength, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            uint low = *(uint*)(statistics + 8);
            int high = *(int*)(statistics + 12);

            return $"Service-0x{high:x}-{low:x}$";
        }
    }

    private static unsafe string Start(string about, SafeAccessTokenHandle token, string program, string desktopName, TimeSpan deadline)
    {
        if (!CreateEnvironmentBlock(out nint environment, token, inherit: false))
        {
            return $"{about} found no environment of the account: {Error(Marshal.GetLastPInvokeError())}";
        }

        try
        {
            // CreateProcessAsUserW may write into the command line, so it gets a copy of its own, ended by a null.
            char[] line = [.. $"\"{program}\"", '\0'];

            fixed (char* desktop = desktopName)
            fixed (char* lineStart = line)
            {
                StartupInfo startup = new() { Size = Marshal.SizeOf<StartupInfo>(), Desktop = (nint)desktop };

                if (!CreateProcessAsUserW(
                    token,
                    program,
                    lineStart,
                    0,
                    0,
                    inheritHandles: false,
                    CreationFlags,
                    environment,
                    Path.GetDirectoryName(program)!,
                    in startup,
                    out ProcessInformation created))
                {
                    return $"{about} failed in CreateProcessAsUserW: {Error(Marshal.GetLastPInvokeError())}";
                }

                try
                {
                    return Ended(about, created.Process, deadline);
                }
                finally
                {
                    CloseHandle(created.Thread);
                    CloseHandle(created.Process);
                }
            }
        }
        finally
        {
            DestroyEnvironmentBlock(environment);
        }
    }

    // How the started program ended; one that does not end within the deadline is ended by the probe.
    private static string Ended(string about, nint process, TimeSpan deadline)
    {
        if (WaitForSingleObject(process, (uint)deadline.TotalMilliseconds) != Signaled)
        {
            TerminateProcess(process, BadInput);

            return $"{about} did not end within {deadline}";
        }

        if (!GetExitCodeProcess(process, out uint exitCode))
        {
            return $"{about} ended with an exit code that cannot be read: {Error(Marshal.GetLastPInvokeError())}";
        }

        return exitCode == BadInput
            ? $"{about} ended with exit code 1, as the worker does for a command line it refuses, so the account runs the program"
            : $"{about} ended with exit code 0x{exitCode:X8}";
    }

    // The code and its message of Windows, which holds the code's meaning alone, never a value given to the call.
    private static string Error(int code)
    {
        return $"{code}, {new Win32Exception(code).Message}";
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

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetTokenInformation(
        SafeAccessTokenHandle token,
        int informationClass,
        void* information,
        int length,
        out int returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(nint process, out uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint process, uint exitCode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
