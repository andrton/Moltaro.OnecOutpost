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
// WorkerCreation, AsUser with the console's own token and WithToken with a worker account's, each in a WorkerJob of its
// own, since a process already in a job, the Secondary Logon service's or a terminal's, joins only an empty job or one
// of its hierarchy. The clock gives the probe's deadline. Every call to Windows goes through LibraryImport, no package
// (principle 12). check calls it from one task at a time, so no two desktops are made at once.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsWorkerStarts(TimeProvider clock) : IWorkerStarts
{
    // INFINITE of WaitForSingleObject, and WAIT_FAILED, what it gives when the wait itself fails.
    private const uint Infinite = uint.MaxValue;

    private const uint WaitFailed = uint.MaxValue;

    // The exit code of a probe ended at its deadline, as Process.Kill gives it.
    private const uint KilledExitCode = uint.MaxValue;

    // What CreateProcessAsUserW and CreateEnvironmentBlock need of the console's own primary token.
    private const TokenAccessLevels OwnTokenAccess =
        TokenAccessLevels.AssignPrimary | TokenAccessLevels.Duplicate | TokenAccessLevels.Query;

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

        using AnonymousPipeServerStream output = new(PipeDirection.In, HandleInheritability.Inheritable);
        Task drained = Task.CompletedTask;

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

                // An anonymous pipe has no asynchronous I/O, so a pool thread reads it.
                drained = Task.Run(() => output.CopyTo(Stream.Null));

                return await ExitCodeWithinAsync(process, deadline, cancellationToken);
            }
        }
        finally
        {
            await drained;
        }
    }

    // A worker of the read mode with three pipes, in a job of its own, which its ComWorkerProcess owns and closes when
    // disposed: under the console's own token by CreateProcessAsUserW, with lpDesktop "", or under the account through
    // the Secondary Logon service, on its desktop. Diagnostics is its standard error read to its end as UTF-8 on a pool
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

    // The worker's process, suspended: WithToken under the account, or AsUser under the console's own token, which
    // CreateProcessAsUserW may assign without a privilege.
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

        using WindowsIdentity own = WindowsIdentity.GetCurrent(OwnTokenAccess);

        return WorkerCreation.CreateSuspended(
            WorkerCallEnum.AsUser,
            own.AccessToken,
            program,
            commandLine,
            "",
            input.ClientSafePipeHandle,
            output.ClientSafePipeHandle,
            error.ClientSafePipeHandle);
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

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeProcessHandle process, uint exitCode);
}
