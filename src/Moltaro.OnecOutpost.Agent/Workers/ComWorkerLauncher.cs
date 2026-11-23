using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The program's launcher of the COM workers: it starts the worker program of the connector's bitness, in the job of
// the agent, and speaks to it through ComWorkerProcess. Start may run on several slots' tasks at once: it only reads the
// registry, and Process.Start makes the pipes and the process under a lock of its own, so that no worker inherits the
// pipes of another started at the same moment, which would keep that one's output open after a kill.
[SupportedOSPlatform("windows")]
internal sealed partial class ComWorkerLauncher : IWorkerLauncher
{
    // The ProgID of the COM connector that the platform registers.
    public const string ComConnector = "V83.COMConnector";

    // The file of the COM worker in x64\ and x86\ beside the agent's program, each with the files of its build.
    public const string WorkerFile = "Moltaro.OnecOutpost.ComWorker.exe";

    private readonly string _x64Program;

    private readonly string _x86Program;

    private readonly string _connectorProgId;

    private readonly WorkerJob _job;

    // The two programs of the worker, the ProgID of the connector whose registration chooses between them, and the job
    // that every started process joins.
    public ComWorkerLauncher(string x64Program, string x86Program, string connectorProgId, WorkerJob job)
    {
        _x64Program = x64Program;
        _x86Program = x86Program;
        _connectorProgId = connectorProgId;
        _job = job;
    }

    // Logs on the worker account of the base, before any Start. Each failure closes the token and throws
    // AgentStopException, never with the password:
    // 1. LogonUserW(account, ".", password, LOGON32_LOGON_BATCH, LOGON32_PROVIDER_DEFAULT), the password in
    //    Marshal.StringToHGlobalUni, zeroed in a finally; BATCH, since a service logon adds SERVICE with
    //    SeImpersonatePrivilege. Failure: the worker account {account} of the base {key} cannot log on: {the Win32
    //    message};
    // 2. the token's groups, whatever their attributes, and privileges, enabled or not, judged by WorkerAccountRule:
    //    the worker account {account} of the base {key} is an administrator or holds a privilege beyond an ordinary
    //    user's; expected a local user that is not in Administrators, as the console's install prepares it;
    // 3. the agent's token holds SeAssignPrimaryTokenPrivilege and SeIncreaseQuotaPrivilege, enabled or not, since
    //    CreateProcessAsUserW enables them: the agent's account lacks the right "Replace a process level token" or
    //    "Adjust memory quotas for a process"; install the agent with the console.
    // The logon comes first, so that a missing account gives 1326 whatever the agent holds. The token lives as long as
    // the launcher; one logon per start of the agent, so no retries lock the account out. Invariant: every COM worker of
    // a logged-on base runs under its token, Reads included; the agent's own token runs only bases without a
    // workerAccount.
    public void LogOn(string baseKey, string account, string password)
    {
        throw new NotSupportedException();
    }

    // Reads the registry at each start (concept §5, O8): the x64 program when the connector is registered for 64 bits,
    // otherwise the x86 program when it is for 32 bits, otherwise a NoProcess. Process.Start of the program with
    // ArgumentList the base key and read or write, its standard input and output redirected, UseShellExecute false and
    // CreateNoWindow true, then WorkerJob.Add. The connection with its password travels only in the first input line,
    // never on the command line or in the environment. A program that does not start, and a process the job does not
    // take, killed at once, give a NoProcess: Start throws nothing. Limitation: a process is in the job from just after
    // it starts; if the agent ends in between, it ends at the end of its input, as S9's worker does.
    public IWorkerProcess Start(string baseKey, OutpostModeEnum mode)
    {
        string? program = ProgramOfConnector();

        if (program is null)
        {
            return new NoProcess();
        }

        // The standard error is not redirected: the agent keeps no diagnostics of a worker, so the worker's go where the
        // agent's own standard error goes, nowhere for the service, and no pipe of the agent fills up or holds a thread
        // for them.
        ProcessStartInfo start = new(program)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.ArgumentList.Add(baseKey);
        start.ArgumentList.Add(mode == OutpostModeEnum.Write ? "write" : "read");

        Process process;

        try
        {
            // Never null without the shell.
            process = Process.Start(start)!;
        }
        catch (Win32Exception)
        {
            // The program is missing, or cannot run.
            return new NoProcess();
        }

        try
        {
            _job.Add(process.SafeHandle);
        }
        catch (Win32Exception)
        {
            // A worker outside the job would outlive the agent.
            process.Kill();
            process.Dispose();

            return new NoProcess();
        }

        return new ComWorkerProcess(process.SafeHandle, process.StandardInput.BaseStream, process.StandardOutput.BaseStream);
    }

    // The program of the bitness for which the connector is registered, 64 bits first, or null for none. A 32-bit
    // Windows shows its one view as the view of 64 bits too, so 64 bits are asked of a 64-bit Windows only.
    private string? ProgramOfConnector()
    {
        if (Environment.Is64BitOperatingSystem && IsRegistered(RegistryView.Registry64))
        {
            return _x64Program;
        }

        if (IsRegistered(RegistryView.Registry32))
        {
            return _x86Program;
        }

        return null;
    }

    // Registered for the bitness of the view: HKEY_CLASSES_ROOT gives a class identifier as the default value of
    // {connectorProgId}\CLSID, and CLSID\{that}\InprocServer32 has a default value that is not empty. A key that cannot
    // be read is not registered; no file or version is checked, the console's check does (S10).
    private bool IsRegistered(RegistryView view)
    {
        try
        {
            using RegistryKey classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
            using RegistryKey? progId = classes.OpenSubKey($@"{_connectorProgId}\CLSID");

            if (progId?.GetValue("") is not string classId || !Guid.TryParse(classId, out _))
            {
                return false;
            }

            using RegistryKey? server = classes.OpenSubKey($@"CLSID\{classId}\InprocServer32");

            return server?.GetValue("") is string path && path.Length > 0;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
