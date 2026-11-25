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

    private readonly bool _useWorkerLogonDesktop;

    // The agent's own primary token.
    private readonly SafeAccessTokenHandle _agentToken;

    // The token and any explicit desktop of each logged-on base. Written by Use before any Start reads it.
    private readonly Dictionary<string, (SafeAccessTokenHandle Token, WorkerDesktop? Desktop)> _workers =
        new(StringComparer.Ordinal);

    // The two programs of the worker, the finder of the connector whose registration chooses between them, and the job
    // that every started process joins. The service uses the worker's batch logon desktop in Session 0, where
    // workers failed to initialize on the agent's explicit desktop. A Win32Exception reports failure to open
    // the agent's primary token.
    public ComWorkerLauncher(
        string x64Program,
        string x86Program,
        IConnectorFinder connectors,
        WorkerJob job,
        bool useWorkerLogonDesktop = false)
    {
        _x64Program = x64Program;
        _x86Program = x86Program;
        _connectors = connectors;
        _job = job;
        _useWorkerLogonDesktop = useWorkerLogonDesktop;

        if (!OpenProcessToken(GetCurrentProcess(), AgentTokenAccess, out _agentToken))
        {
            int error = Marshal.GetLastPInvokeError();

            _agentToken.Dispose();

            throw new Win32Exception(error);
        }
    }

    // Logs on the worker account of the base, before any Start. Each failure closes the token and throws
    // AgentStopException, never with the password, with the reasons that the console's check shares
    // (WorkerAccountReasons):
    // 1. WorkerLogon.LogOn, a batch logon whose password is zeroed after the call. Failure: the worker account {account}
    //    of the base {key} cannot log on: {WorkerAccountReasons.CannotLogOn}, which is also the reason when the token's
    //    groups or privileges cannot be read;
    // 2. WorkerLogon.IsOrdinary: the worker account {account} of the base {key} {WorkerAccountReasons.NotOrdinary};
    // 3. the agent's token holds AccountRightNames.AgentPrivileges, enabled or not, since CreateProcessAsUserW enables
    //    them: the agent's account {WorkerAccountReasons.AgentLacksPrivileges}. When the agent's privileges cannot be
    //    read: the privileges of the agent's account cannot be read: {the Win32 message};
    // 4. outside the service, WorkerDesktop.Make for the token: the worker account {account} of the base {key} cannot
    //    get a desktop: {the Win32 message}. Use stores the token and any explicit desktop.
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
                throw new AgentStopException($"the worker account {account} of the base {baseKey} {WorkerAccountReasons.NotOrdinary}");
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
                throw new AgentStopException($"the agent's account {WorkerAccountReasons.AgentLacksPrivileges}");
            }

            WorkerDesktop? desktop;

            try
            {
                desktop = _useWorkerLogonDesktop ? null : WorkerDesktop.Make(baseKey, token);
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
                desktop?.Dispose();
                throw;
            }
        }
        catch
        {
            token.Dispose();
            throw;
        }
    }

    // Selects the token and any explicit desktop for every later Start of the base. A null desktop lets Windows
    // select the desktop of the token's logon session. The launcher owns the handles after this call. A base given
    // twice is an ArgumentException and the caller keeps its handles. Like LogOn, this runs before any Start.
    public void Use(string baseKey, SafeAccessTokenHandle token, WorkerDesktop? desktop)
    {
        _workers.Add(baseKey, (token, desktop));
    }

    // Closes the logged-on bases' tokens and explicit desktops, and the agent's own token.
    public void Dispose()
    {
        foreach ((SafeAccessTokenHandle token, WorkerDesktop? desktop) in _workers.Values)
        {
            token.Dispose();
            desktop?.Dispose();
        }

        _agentToken.Dispose();
    }

    // Asks the connector's finder at each start (concept §5, O8): the x64 program when the connector is registered for 64 bits,
    // otherwise the x86 program when it is for 32 bits, otherwise a NoProcess. Then ComWorkerProcess.Start, AsUser with
    // the token that Use gave for the base, else the agent's own, and lpDesktop the explicit desktop's name if given.
    // Otherwise lpDesktop is "" so Windows selects the desktop for the logon session of the token. The worker gets no
    // standard error and joins the launcher's job, which it does not own and which lets nothing break away. The
    // worker inherits the ends of its two pipes alone, joins the job before any of its code runs, and gets its
    // connection, with its password, only in the first input line. A Win32Exception or an IOException, a created process
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

        if (_workers.TryGetValue(baseKey, out (SafeAccessTokenHandle Token, WorkerDesktop? Desktop) worker))
        {
            token = worker.Token;
            desktop = worker.Desktop?.Name ?? "";
        }

        try
        {
            return ComWorkerProcess.Start(WorkerCallEnum.AsUser, token, program, baseKey, mode, desktop, _job, ownsJob: false, error: null);
        }
        catch (Exception exception) when (exception is IOException or Win32Exception)
        {
            // A pipe that Windows could not make, or a process that it did not create, or that the job did not take.
            return new NoProcess();
        }
    }

    // The sentence of the code, which holds no value given to the call.
    private static AgentStopException CannotLogOn(string baseKey, string account, int error)
    {
        return new AgentStopException(
            $"the worker account {account} of the base {baseKey} cannot log on: {WorkerAccountReasons.CannotLogOn(baseKey, error)}");
    }

    // The program of the first bitness whose finding has a registered connector, 64 bits first, or null for none.
    private string? ProgramOfConnector()
    {
        ConnectorFinding? first = ConnectorFinding.FirstRegistered(_connectors.Find());

        if (first is null)
        {
            return null;
        }

        return first.Bitness == ConnectorBitnessEnum.X64 ? _x64Program : _x86Program;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);
}
