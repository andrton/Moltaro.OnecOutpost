using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The program's launcher of the COM workers: it logs on the worker account of each base whose policy names one, starts
// the worker program of the connector's bitness in the job of the agent, and speaks to it through ComWorkerProcess.
// Every call to Windows goes through LibraryImport, no package (principle 12).
//
// Invariant: every COM worker of a logged-on base runs under the token of its worker account, Reads included; the
// agent's own token, opened by the constructor, runs only the bases without a workerAccount. LogOn runs on the start's
// thread before any Start, so the tokens are only read once workers start. Start may run on several slots' tasks at
// once, without a lock: it asks the finder and reads the tokens, and a worker inherits the ends of its own two pipes alone,
// never those of another started at the same moment. Nothing else in the agent starts a process, which would inherit
// every handle that may be inherited, those ends included, while they are open. The tokens and the desktops of the
// workers live as long as the launcher, which closes them when disposed; the program never disposes it, so Windows
// closes them when the agent ends.
//
// Limitations of a script without safe mode, whose boundary is the rights of the worker account (D12): it can leave a
// process outside the job through a service that the account may use, such as Task Scheduler or WMI; and the workers of
// one base run under one account, so they can read each other's memory, commands and 1C passwords included, and forge
// each other's lines through duplicated handles.
[SupportedOSPlatform("windows")]
internal sealed partial class ComWorkerLauncher : IWorkerLauncher, IDisposable
{
    // TOKEN_ASSIGN_PRIMARY, TOKEN_DUPLICATE and TOKEN_QUERY: what CreateProcessAsUserW and CreateEnvironmentBlock need of
    // a primary token, and what GetTokenInformation needs.
    private const uint AgentTokenAccess = 0x0001 | 0x0002 | 0x0008;

    private readonly string _x64Program;

    private readonly string _x86Program;

    private readonly IConnectorFinder _connectors;

    private readonly WorkerJob _job;

    // The agent's own primary token.
    private readonly SafeAccessTokenHandle _agentToken;

    // The token and the desktop of each logged-on base, by base key. Written only by Use, before any Start reads it.
    private readonly Dictionary<string, (SafeAccessTokenHandle Token, WorkerDesktop Desktop)> _workers =
        new(StringComparer.Ordinal);

    // The two programs of the worker, the finder of the connector whose registration chooses between them, and the job
    // that every started process joins. It opens the agent's own primary token; a failure is a Win32Exception.
    public ComWorkerLauncher(string x64Program, string x86Program, IConnectorFinder connectors, WorkerJob job)
    {
        _x64Program = x64Program;
        _x86Program = x86Program;
        _connectors = connectors;
        _job = job;

        if (!OpenProcessToken(GetCurrentProcess(), AgentTokenAccess, out _agentToken))
        {
            int error = Marshal.GetLastPInvokeError();

            _agentToken.Dispose();

            throw new Win32Exception(error);
        }
    }

    // Logs on the worker account of the base, before any Start. Each failure closes the token and throws
    // AgentStopException, never with the password:
    // 1. WorkerLogon.LogOn, a batch logon whose password is zeroed after the call. Failure: the worker account {account}
    //    of the base {key} cannot log on: {the Win32 message}, which is also the reason when the token's groups or
    //    privileges cannot be read;
    // 2. WorkerLogon.IsOrdinary: the worker account {account} of the base {key} is an administrator or holds a privilege
    //    beyond an ordinary user's; expected a local user that is not in Administrators, as the console's install
    //    prepares it;
    // 3. the agent's token holds AccountRightNames.AgentPrivileges, enabled or not, since
    //    CreateProcessAsUserW enables them: the agent's account lacks the right "Replace a process level token" or
    //    "Adjust memory quotas for a process"; install the agent with the console. When the agent's privileges cannot
    //    be read: the privileges of the agent's account cannot be read: {the Win32 message};
    // 4. WorkerDesktop.Make for the token, then Use with both (the S9c brief, "The desktop of a worker"): the worker
    //    account {account} of the base {key} cannot get a desktop: {the Win32 message}.
    // The logon comes first, so that a missing account gives 1326 whatever the agent holds. The token and the desktop
    // live as long as the launcher; one logon per start of the agent, so no retries lock the account out.
    public void LogOn(string baseKey, string account, string password)
    {
        SafeAccessTokenHandle token = WorkerLogon.LogOn(account, password, out int error)
            ?? throw CannotLogOn(baseKey, account, error);

        try
        {
            bool ordinary;

            try
            {
                ordinary = WorkerLogon.IsOrdinary(token);
            }
            catch (Win32Exception exception)
            {
                throw CannotLogOn(baseKey, account, exception.NativeErrorCode);
            }

            if (!ordinary)
            {
                throw new AgentStopException(
                    $"the worker account {account} of the base {baseKey} is an administrator or holds a privilege beyond an ordinary user's; expected a local user that is not in Administrators, as the console's install prepares it");
            }

            bool agentMayStart;

            try
            {
                List<string> agentPrivileges = WorkerToken.Privileges(_agentToken);

                agentMayStart = AccountRightNames.AgentPrivileges.All(agentPrivileges.Contains);
            }
            catch (Win32Exception exception)
            {
                throw new AgentStopException($"the privileges of the agent's account cannot be read: {exception.Message}");
            }

            if (!agentMayStart)
            {
                throw new AgentStopException(
                    "the agent's account lacks the right \"Replace a process level token\" or \"Adjust memory quotas for a process\"; install the agent with the console");
            }

            WorkerDesktop desktop;

            try
            {
                desktop = WorkerDesktop.Make(baseKey, token);
            }
            catch (Win32Exception exception)
            {
                throw new AgentStopException(
                    $"the worker account {account} of the base {baseKey} cannot get a desktop: {exception.Message}");
            }

            try
            {
                Use(baseKey, token, desktop);
            }
            catch
            {
                desktop.Dispose();
                throw;
            }
        }
        catch
        {
            token.Dispose();
            throw;
        }
    }

    // Makes every later Start of the base start its workers under the token, on the desktop that lpDesktop names as
    // desktop.Name. The launcher owns both from then on and closes them when disposed. LogOn calls it after its checks,
    // with the desktop that WorkerDesktop.Make gives for the token; a test calls it with its own token and a desktop of
    // its own. Like LogOn, it runs before any Start.
    // A base given twice is an ArgumentException, and then the caller keeps both.
    public void Use(string baseKey, SafeAccessTokenHandle token, WorkerDesktop desktop)
    {
        _workers.Add(baseKey, (token, desktop));
    }

    // Closes the tokens of the logged-on bases, their desktops and the agent's own token.
    public void Dispose()
    {
        foreach ((SafeAccessTokenHandle token, WorkerDesktop desktop) in _workers.Values)
        {
            token.Dispose();
            desktop.Dispose();
        }

        _agentToken.Dispose();
    }

    // Asks the connector's finder at each start (concept §5, O8): the x64 program when the connector is registered for 64 bits,
    // otherwise the x86 program when it is for 32 bits, otherwise a NoProcess. Two anonymous pipes, then
    // CreateProcessAsUserW with the token that Use gave for the base, else the agent's own: the program's full path, the
    // command line "{program}" {base key} {read|write}, suspended and without a window, the environment of the token's
    // account, CreateEnvironmentBlock(token, false), never the agent's, the program's folder as its working folder,
    // lpDesktop the Name of the base's desktop, or "" under the agent's own token, which connects the worker to the
    // window station of the agent's logon session, the client ends of the pipes as its standard input and output, a
    // null standard error, and a list of the handles it inherits that names those two ends alone. Then the agent's
    // copies of the client ends close, the process joins the job and its thread resumes, so that a worker is in the job
    // before any of its code runs; the job lets nothing break away from it. The connection with its password travels
    // only in the first input line, never on the command line or in the environment. Any failure, a created process
    // terminated first, gives a NoProcess: Start throws nothing but the ObjectDisposedException of a job that was
    // disposed, which only a test does. Limitations: the profile of a worker account is not loaded, since only an
    // administrator or LocalSystem may load one, so its HKCU is the default hive and its TEMP the system's; and a worker
    // that does not start keeps no diagnostics.
    public IWorkerProcess Start(string baseKey, OutpostModeEnum mode)
    {
        string? program = ProgramOfConnector();

        if (program is null)
        {
            return new NoProcess();
        }

        SafeAccessTokenHandle token = _agentToken;
        string desktop = "";

        if (_workers.TryGetValue(baseKey, out (SafeAccessTokenHandle Token, WorkerDesktop Desktop) worker))
        {
            token = worker.Token;
            desktop = worker.Desktop.Name;
        }

        string commandLine = $"\"{program}\" {baseKey} {OutpostForms.WriteMode(mode)}";

        AnonymousPipeServerStream? input = null;
        AnonymousPipeServerStream? output = null;

        try
        {
            input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

            (SafeProcessHandle process, nint thread) = WorkerCreation.CreateSuspended(
                WorkerCallEnum.AsUser,
                token,
                program,
                commandLine,
                desktop,
                input.ClientSafePipeHandle,
                output.ClientSafePipeHandle,
                error: null);

            // The worker holds the client ends now: once the agent's copies close, its end closes its output.
            input.DisposeLocalCopyOfClientHandle();
            output.DisposeLocalCopyOfClientHandle();

            return JoinAndResume(new ComWorkerProcess(process, input, output), process, thread);
        }
        catch (Exception exception) when (exception is IOException or Win32Exception)
        {
            // A pipe that Windows could not make, or a process that it did not create.
        }

        input?.Dispose();
        output?.Dispose();

        return new NoProcess();
    }

    // The message of Windows for its error code holds the code's meaning alone, never a value given to the call.
    private static AgentStopException CannotLogOn(string baseKey, string account, int error)
    {
        return new AgentStopException(
            $"the worker account {account} of the base {baseKey} cannot log on: {new Win32Exception(error).Message}");
    }

    // Puts the suspended worker into the job and resumes its thread by WorkerCreation.JoinAndResume, which terminates a
    // process that the job does not take or whose thread does not resume: that worker is disposed, a NoProcess. Any other
    // exception, such as the ObjectDisposedException of a disposed job, passes after the dispose, so that no process is
    // ever left outside the job.
    private IWorkerProcess JoinAndResume(ComWorkerProcess worker, SafeProcessHandle process, nint thread)
    {
        try
        {
            WorkerCreation.JoinAndResume(_job, process, thread);

            return worker;
        }
        catch (Win32Exception)
        {
            worker.Dispose();

            return new NoProcess();
        }
        catch
        {
            worker.Dispose();
            throw;
        }
    }

    // The program of the first bitness whose finding has a registered connector, 64 bits first, or null for none.
    private string? ProgramOfConnector()
    {
        foreach (ConnectorFinding finding in _connectors.Find())
        {
            if (finding.Registered is null)
            {
                continue;
            }

            return finding.Bitness == ConnectorBitnessEnum.X64 ? _x64Program : _x86Program;
        }

        return null;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);
}
