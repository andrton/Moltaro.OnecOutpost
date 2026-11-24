using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Console.Workers;

// The COM workers of check on Windows: the logon by WorkerLogon, the desktop by WorkerDesktop.Make, the process by
// WorkerCreation, AsUser with a restricted version of the console's own token and WithToken with a worker account's,
// each in a WorkerJob of its own, since a process already in a job, the Secondary Logon service's or a terminal's,
// joins only an empty job or one of its hierarchy. The clock gives the probe's deadline. Every call to Windows goes
// through LibraryImport, no package (principle 12). check calls it from one task at a time, so no two desktops are
// made at once.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsWorkerStarts(TimeProvider clock) : IWorkerStarts
{
    // INFINITE of WaitForSingleObject, and WAIT_FAILED, what it gives when the wait itself fails.
    private const uint Infinite = uint.MaxValue;

    private const uint WaitFailed = uint.MaxValue;

    // The exit code of a probe ended at its deadline, as Process.Kill gives it.
    private const uint KilledExitCode = uint.MaxValue;

    // What CreateRestrictedToken needs of the console's own primary token, and SetTokenInformation, CreateProcessAsUserW
    // and CreateEnvironmentBlock of the restricted one, which has the same access: TOKEN_ADJUST_DEFAULT sets its
    // integrity level.
    private const TokenAccessLevels OwnTokenAccess =
        TokenAccessLevels.AssignPrimary
        | TokenAccessLevels.Duplicate
        | TokenAccessLevels.Query
        | TokenAccessLevels.AdjustDefault;

    // DISABLE_MAX_PRIVILEGE of CreateRestrictedToken: every privilege but SeChangeNotifyPrivilege deleted.
    private const uint DisableMaxPrivilege = 0x1;

    // TokenIntegrityLevel of TOKEN_INFORMATION_CLASS, the medium level's label S-1-16-8192, and SE_GROUP_INTEGRITY, the
    // attribute of a label.
    private const int TokenIntegrityLevelClass = 25;

    private const string MediumIntegrity = "S-1-16-8192";

    private const uint GroupIntegrity = 0x20;

    // The clock of the probe's deadline.
    private TimeProvider Clock { get; } = clock;

    // WorkerLogon.LogOn, whose password reaches only LogonUserW; a failure is the Win32Exception of its code.
    public IWorkerAccount LogOn(string baseKey, string account, string password)
    {
        SafeAccessTokenHandle token = WorkerLogon.LogOn(account, password, out int error) ?? throw new Win32Exception(error);

        return Account(baseKey, token);
    }

    // The worker of the write mode under the account on its desktop, through the Secondary Logon service, with two pipes
    // and no standard error. The console's end of the input closes before the worker resumes, so that its first line is
    // the end of its input, at which it ends with WorkerExitCodes.Ended before any COM call. Its exit is raced with the
    // deadline on the clock, TerminateProcess at it; its output is read to its end, awaited after its job is closed.
    public async Task<uint?> ProbeAsync(
        IWorkerAccount account,
        string program,
        string baseKey,
        TimeSpan deadline,
        CancellationToken cancellationToken)
    {
        WindowsWorkerAccount worker = (WindowsWorkerAccount)account;
        string desktop = worker.Desktop();
        string commandLine = CommandLineOf(program, baseKey, OutpostModeEnum.Write);

        // Owned by the drain once it reads it, and disposed here before.
        AnonymousPipeServerStream output = new(PipeDirection.In, HandleInheritability.Inheritable);
        Task? drained = null;

        try
        {
            // Closed when the try block ends, before the output is awaited: it ends the worker if it still runs.
            using WorkerJob job = new();
            SafeProcessHandle process;
            nint thread;

            using (AnonymousPipeServerStream input = new(PipeDirection.Out, HandleInheritability.Inheritable))
            {
                try
                {
                    (process, thread) = WorkerCreation.CreateSuspended(
                        WorkerCallEnum.WithToken,
                        worker.Token,
                        program,
                        commandLine,
                        desktop,
                        input.ClientSafePipeHandle,
                        output.ClientSafePipeHandle,
                        error: null);
                }
                finally
                {
                    // The worker holds the client ends now, or none was made: a pipe's dispose leaves its client end
                    // open once it was handed out.
                    input.DisposeLocalCopyOfClientHandle();
                    output.DisposeLocalCopyOfClientHandle();
                }
            }

            using (process)
            {
                WorkerCreation.JoinAndResume(job, process, thread);

                drained = DrainAsync(output);

                return await ExitCodeWithinAsync(process, deadline, cancellationToken);
            }
        }
        finally
        {
            if (drained is null)
            {
                output.Dispose();
            }
            else
            {
                await drained.WaitAsync(cancellationToken);
            }
        }
    }

    // A worker of the read mode with three pipes, in a job of its own, which its ComWorkerProcess owns and closes when
    // disposed: under a restricted version of the console's own token by CreateProcessAsUserW, with lpDesktop "", or
    // under the account through the Secondary Logon service, on its desktop. Diagnostics is its standard error read to its end as UTF-8 on a pool
    // thread, which ends when the worker ends, its job closed at the latest.
    public CheckWorker Start(IWorkerAccount? account, string program, string baseKey)
    {
        string commandLine = CommandLineOf(program, baseKey, OutpostModeEnum.Read);
        WorkerJob job = new();
        AnonymousPipeServerStream? input = null;
        AnonymousPipeServerStream? output = null;
        AnonymousPipeServerStream? error = null;
        ComWorkerProcess? worker = null;

        try
        {
            input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            error = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

            (SafeProcessHandle process, nint thread) = Create(account, program, commandLine, input, output, error);

            // The worker holds the client ends now: once the console's copies close, its ends close with it.
            input.DisposeLocalCopyOfClientHandle();
            output.DisposeLocalCopyOfClientHandle();
            error.DisposeLocalCopyOfClientHandle();

            worker = new ComWorkerProcess(process, input, output, job);
            WorkerCreation.JoinAndResume(job, process, thread);

            return new CheckWorker(worker, DiagnosticsOf(error));
        }
        catch
        {
            // A worker that was made owns its streams, its process and its job; before it, each goes by itself.
            if (worker is null)
            {
                Close(input);
                Close(output);
                job.Dispose();
            }
            else
            {
                worker.Dispose();
            }

            Close(error);
            throw;
        }
    }

    // The account of a token that the caller logged on itself, such as the process's own in a test, which it then owns.
    public IWorkerAccount Account(string baseKey, SafeAccessTokenHandle token)
    {
        return new WindowsWorkerAccount(baseKey, token);
    }

    // "{program}" {base key} {read|write}, as the agent's launcher starts a worker.
    private static string CommandLineOf(string program, string baseKey, OutpostModeEnum mode)
    {
        return $"\"{program}\" {baseKey} {OutpostForms.WriteMode(mode)}";
    }

    // The worker's process, suspended: WithToken under the account, or AsUser under the restricted version of the
    // console's own token, which CreateProcessAsUserW assigns without "Replace a process level token", since it is a
    // restricted version of the caller's primary token.
    private static (SafeProcessHandle Process, nint Thread) Create(
        IWorkerAccount? account,
        string program,
        string commandLine,
        AnonymousPipeServerStream input,
        AnonymousPipeServerStream output,
        AnonymousPipeServerStream error)
    {
        if (account is not null)
        {
            WindowsWorkerAccount worker = (WindowsWorkerAccount)account;

            return WorkerCreation.CreateSuspended(
                WorkerCallEnum.WithToken,
                worker.Token,
                program,
                commandLine,
                worker.Desktop(),
                input.ClientSafePipeHandle,
                output.ClientSafePipeHandle,
                error.ClientSafePipeHandle);
        }

        using SafeAccessTokenHandle restricted = RestrictedOwnToken();

        return WorkerCreation.CreateSuspended(
            WorkerCallEnum.AsUser,
            restricted,
            program,
            commandLine,
            "",
            input.ClientSafePipeHandle,
            output.ClientSafePipeHandle,
            error.ClientSafePipeHandle);
    }

    // The console's own primary token restricted, so that the code that 1C runs when it opens a base, which somebody
    // else may have written, runs without the rights of an administrator and cannot reach the elevated terminal of
    // check: DISABLE_MAX_PRIVILEGE, Administrators for deny only, when the token holds it, and medium integrity, so that
    // User Interface Privilege Isolation keeps it from the elevated programs of the desktop. It is still the
    // administrator's own account, unelevated, with its profile and the secrets DPAPI keeps for it. Lowering the level
    // needs no privilege. A failure is a Win32Exception.
    private static unsafe SafeAccessTokenHandle RestrictedOwnToken()
    {
        using WindowsIdentity own = WindowsIdentity.GetCurrent(OwnTokenAccess);
        SecurityIdentifier administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        byte[] sid = new byte[administrators.BinaryLength];
        administrators.GetBinaryForm(sid, 0);

        fixed (byte* sidStart = sid)
        {
            SidAndAttributes denyOnly = new() { Sid = (nint)sidStart };

            if (!CreateRestrictedToken(own.AccessToken, DisableMaxPrivilege, 1, &denyOnly, 0, 0, 0, 0, out SafeAccessTokenHandle restricted))
            {
                int error = Marshal.GetLastPInvokeError();

                restricted.Dispose();

                throw new Win32Exception(error);
            }

            try
            {
                SetMediumIntegrity(restricted);
            }
            catch
            {
                restricted.Dispose();
                throw;
            }

            return restricted;
        }
    }

    // TOKEN_MANDATORY_LABEL, which is one SID_AND_ATTRIBUTES, with the label of the medium level; its length counts the
    // SID it points at, as the call expects.
    private static unsafe void SetMediumIntegrity(SafeAccessTokenHandle token)
    {
        SecurityIdentifier medium = new(MediumIntegrity);
        byte[] sid = new byte[medium.BinaryLength];
        medium.GetBinaryForm(sid, 0);

        fixed (byte* sidStart = sid)
        {
            SidAndAttributes label = new() { Sid = (nint)sidStart, Attributes = GroupIntegrity };
            int length = sizeof(SidAndAttributes) + sid.Length;

            if (!SetTokenInformation(token, TokenIntegrityLevelClass, &label, length))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }

    // Reads the probe's output to its end on a pool thread, since an anonymous pipe has no asynchronous I/O, and then
    // disposes it, so that nothing disposes the pipe while the read may still run.
    private static Task DrainAsync(AnonymousPipeServerStream output)
    {
        return Task.Run(() =>
        {
            using (output)
            {
                output.CopyTo(Stream.Null);
            }
        });
    }

    // A pipe and the console's copy of its client end, which the pipe's dispose leaves open once it was handed out.
    private static void Close(AnonymousPipeServerStream? pipe)
    {
        pipe?.DisposeLocalCopyOfClientHandle();
        pipe?.Dispose();
    }

    private static Task<string> DiagnosticsOf(AnonymousPipeServerStream error)
    {
        return Task.Run(() =>
        {
            using StreamReader reader = new(error, Encoding.UTF8);

            return reader.ReadToEnd();
        });
    }

    // The exit code once the process ends, waited for on a pool thread; a failed wait is a Win32Exception.
    private static uint ExitCodeOf(SafeProcessHandle process)
    {
        if (WaitForSingleObject(process, Infinite) == WaitFailed || !GetExitCodeProcess(process, out uint exitCode))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return exitCode;
    }

    // The exit code of the process, or null once it was ended at the deadline: its end is raced with the deadline on
    // the clock, whose timer goes with the race. A cancellation ends the process too, and then passes. The process has
    // ended whenever this returns, so the wait of the pool thread is over before its handle closes.
    private async Task<uint?> ExitCodeWithinAsync(SafeProcessHandle process, TimeSpan deadline, CancellationToken cancellationToken)
    {
        Task<uint> exited = Task.Run(() => ExitCodeOf(process), CancellationToken.None);

        using (CancellationTokenSource race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            Task limit = Task.Delay(deadline, Clock, race.Token);

            if (await Task.WhenAny(exited, limit) == exited)
            {
                race.Cancel();

                return await exited;
            }
        }

        // The handle of a created process may terminate it, so the call fails only for one that has ended.
        TerminateProcess(process, KilledExitCode);
        await exited;
        cancellationToken.ThrowIfCancellationRequested();

        return null;
    }

    // The SIDs to disable become deny-only groups of the new token; neither a privilege nor a restricting SID is given.
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CreateRestrictedToken(
        SafeAccessTokenHandle existingToken,
        uint flags,
        uint disableSidCount,
        SidAndAttributes* sidsToDisable,
        uint deletePrivilegeCount,
        nint privilegesToDelete,
        uint restrictedSidCount,
        nint sidsToRestrict,
        out SafeAccessTokenHandle newToken);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetTokenInformation(
        SafeAccessTokenHandle token,
        int informationClass,
        SidAndAttributes* information,
        int length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeProcessHandle process, uint exitCode);
}
