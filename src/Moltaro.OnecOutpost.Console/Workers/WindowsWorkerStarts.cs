using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Console.Workers;

// The COM workers of check and metadata on Windows: the logon by WorkerLogon, the desktop by WorkerDesktop.Make, the
// process by WorkerCreation, AsUser with a restricted version of the console's own token and WithToken with a worker
// account's, each in a WorkerJob of its own, since a process already in a job, the Secondary Logon service's or a
// terminal's, joins only an empty job or one of its hierarchy. The clock gives the probe's deadline. Every call to
// Windows goes through LibraryImport, no package (principle 12). Each command calls it from one task at a time, so no
// two desktops are made at once.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsWorkerStarts(TimeProvider clock) : IWorkerStarts
{
    // What CreateRestrictedToken needs of the console's own primary token, and SetTokenInformation, CreateProcessAsUserW
    // and CreateEnvironmentBlock of the restricted one, which has the same access: TOKEN_ADJUST_DEFAULT sets its default
    // owner, its default list and its integrity level.
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

    // TokenOwner and TokenDefaultDacl of TOKEN_INFORMATION_CLASS, and GENERIC_ALL, which the default list gives.
    private const int TokenOwnerClass = 4;

    private const int TokenDefaultDaclClass = 6;

    private const int GenericAll = 0x10000000;

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
        string commandLine = ComWorkerProcess.CommandLineOf(program, baseKey, OutpostModeEnum.Write);

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

                return await WorkerCreation.ExitCodeWithinAsync(process, deadline, Clock, cancellationToken);
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

    // A worker of the read mode, by ComWorkerProcess.Start, in a job of its own, which the worker owns and closes when
    // disposed, with an error pipe of the console's: under a restricted version of the console's own token by
    // CreateProcessAsUserW, with lpDesktop "", or under the account through the Secondary Logon service, on its desktop.
    // Diagnostics is its standard error read to its end as UTF-8 on a pool thread, which ends when the worker ends, its
    // job closed at the latest.
    public ConsoleWorker Start(IWorkerAccount? account, string program, string baseKey)
    {
        WorkerJob job = new();
        AnonymousPipeServerStream? error = null;

        try
        {
            error = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

            ComWorkerProcess worker = StartWorker(account, program, baseKey, job, error);

            return new ConsoleWorker(worker, DiagnosticsOf(error));
        }
        catch
        {
            // No worker was made, so the job and the error pipe are still the console's.
            job.Dispose();
            Close(error);
            throw;
        }
    }

    // The account of a token that the caller logged on itself, such as the process's own in a test, which it then owns.
    public IWorkerAccount Account(string baseKey, SafeAccessTokenHandle token)
    {
        return new WindowsWorkerAccount(baseKey, token);
    }

    // The worker's process: WithToken under the account, or AsUser under the restricted version of the console's own
    // token, which CreateProcessAsUserW assigns without "Replace a process level token", since it is a restricted version
    // of the caller's primary token.
    private static ComWorkerProcess StartWorker(
        IWorkerAccount? account,
        string program,
        string baseKey,
        WorkerJob job,
        AnonymousPipeServerStream error)
    {
        if (account is not null)
        {
            WindowsWorkerAccount worker = (WindowsWorkerAccount)account;

            return ComWorkerProcess.Start(
                WorkerCallEnum.WithToken,
                worker.Token,
                program,
                baseKey,
                OutpostModeEnum.Read,
                worker.Desktop(),
                job,
                ownsJob: true,
                error);
        }

        using SafeAccessTokenHandle restricted = RestrictedOwnToken();

        return ComWorkerProcess.Start(
            WorkerCallEnum.AsUser,
            restricted,
            program,
            baseKey,
            OutpostModeEnum.Read,
            "",
            job,
            ownsJob: true,
            error);
    }

    // The console's own primary token restricted, so that the code that 1C runs when it opens a base, which somebody
    // else may have written, runs without the rights of an administrator and cannot reach the elevated terminal of
    // check: DISABLE_MAX_PRIVILEGE, Administrators for deny only, when the token holds it, and medium integrity, so that
    // User Interface Privilege Isolation keeps it from the elevated programs of the desktop. Its default owner and list
    // are the user's, as a token that is not elevated has them: an elevated token's, Administrators and a list for
    // Administrators and SYSTEM, would keep the worker, which holds Administrators for deny only, from reopening what it
    // and 1C create without a descriptor of their own. It is still the administrator's own account, unelevated, with its
    // profile and the secrets DPAPI keeps for it. These changes need no privilege. A failure is a Win32Exception.
    private static unsafe SafeAccessTokenHandle RestrictedOwnToken()
    {
        using WindowsIdentity own = WindowsIdentity.GetCurrent(OwnTokenAccess);
        SecurityIdentifier user = own.User!;
        byte[] sid = BinaryOf(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));

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
                SetUserDefaults(restricted, user);
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

    // TOKEN_OWNER with the user, who may always own what the token creates, and TOKEN_DEFAULT_DACL with a list that
    // gives the user and SYSTEM all access; each is a pointer, and each length counts what it points at.
    private static unsafe void SetUserDefaults(SafeAccessTokenHandle token, SecurityIdentifier user)
    {
        RawAcl list = new(GenericAcl.AclRevision, 2);
        list.InsertAce(0, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, GenericAll, user, false, null));
        list.InsertAce(
            1,
            new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, GenericAll, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), false, null));

        byte[] sid = BinaryOf(user);
        byte[] acl = new byte[list.BinaryLength];
        list.GetBinaryForm(acl, 0);

        fixed (byte* sidStart = sid)
        fixed (byte* aclStart = acl)
        {
            nint owner = (nint)sidStart;
            nint defaultDacl = (nint)aclStart;

            if (!SetTokenInformation(token, TokenOwnerClass, &owner, sizeof(nint) + sid.Length)
                || !SetTokenInformation(token, TokenDefaultDaclClass, &defaultDacl, sizeof(nint) + acl.Length))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }

    // TOKEN_MANDATORY_LABEL, which is one SID_AND_ATTRIBUTES, with the label of the medium level; its length counts the
    // SID it points at, as the call expects.
    private static unsafe void SetMediumIntegrity(SafeAccessTokenHandle token)
    {
        byte[] sid = BinaryOf(new SecurityIdentifier(MediumIntegrity));

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

    private static byte[] BinaryOf(SecurityIdentifier sid)
    {
        byte[] binary = new byte[sid.BinaryLength];
        sid.GetBinaryForm(binary, 0);

        return binary;
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
        void* information,
        int length);
}
