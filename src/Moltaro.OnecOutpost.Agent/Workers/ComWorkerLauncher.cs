using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The program's launcher of the COM workers: it logs on the worker account of each base whose policy names one, starts
// the worker program of the connector's bitness in the job of the agent, and speaks to it through ComWorkerProcess.
// Every call to Windows goes through LibraryImport, no package (principle 12).
//
// Invariant: every COM worker of a logged-on base runs under the token of its worker account, Reads included; the
// agent's own token, opened by the constructor, runs only the bases without a workerAccount. LogOn runs on the start's
// thread before any Start, so the tokens are only read once workers start. Start may run on several slots' tasks at
// once, without a lock: it reads the registry and the tokens, and a worker inherits the ends of its own two pipes alone,
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
    // The ProgID of the COM connector that the platform registers.
    public const string ComConnector = "V83.COMConnector";

    // The file of the COM worker in x64\ and x86\ beside the agent's program, each with the files of its build.
    public const string WorkerFile = "Moltaro.OnecOutpost.ComWorker.exe";

    // LOGON32_LOGON_BATCH and LOGON32_PROVIDER_DEFAULT.
    private const int BatchLogon = 4;

    private const int DefaultProvider = 0;

    // TOKEN_ASSIGN_PRIMARY, TOKEN_DUPLICATE and TOKEN_QUERY: what CreateProcessAsUserW and CreateEnvironmentBlock need of
    // a primary token, and what GetTokenInformation needs.
    private const uint AgentTokenAccess = 0x0001 | 0x0002 | 0x0008;

    // TokenGroups and TokenPrivileges of TOKEN_INFORMATION_CLASS.
    private const int TokenGroupsClass = 2;

    private const int TokenPrivilegesClass = 3;

    // CREATE_SUSPENDED, CREATE_UNICODE_ENVIRONMENT, EXTENDED_STARTUPINFO_PRESENT and CREATE_NO_WINDOW.
    private const uint CreationFlags = 0x00000004 | 0x00000400 | 0x00080000 | 0x08000000;

    // STARTF_USESTDHANDLES.
    private const int UseStandardHandles = 0x00000100;

    // PROC_THREAD_ATTRIBUTE_HANDLE_LIST: the only handles that the process inherits.
    private const nuint HandleListAttribute = 0x00020002;

    // What ResumeThread gives when it fails, (DWORD)-1.
    private const uint ResumeFailed = uint.MaxValue;

    // The privileges that CreateProcessAsUserW needs to start a process under a token that is not the caller's own:
    // "Replace a process level token" and "Adjust memory quotas for a process".
    private const string AssignPrimaryToken = "SeAssignPrimaryTokenPrivilege";

    private const string IncreaseQuota = "SeIncreaseQuotaPrivilege";

    private readonly string _x64Program;

    private readonly string _x86Program;

    private readonly string _connectorProgId;

    private readonly WorkerJob _job;

    // The agent's own primary token.
    private readonly SafeAccessTokenHandle _agentToken;

    // The token of each logged-on base, by base key. Written only by LogOn, before any Start reads it.
    private readonly Dictionary<string, SafeAccessTokenHandle> _tokens = new(StringComparer.Ordinal);

    // The two programs of the worker, the ProgID of the connector whose registration chooses between them, and the job
    // that every started process joins. It opens the agent's own primary token; a failure is a Win32Exception.
    public ComWorkerLauncher(string x64Program, string x86Program, string connectorProgId, WorkerJob job)
    {
        _x64Program = x64Program;
        _x86Program = x86Program;
        _connectorProgId = connectorProgId;
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
    // 1. LogonUserW(account, ".", password, LOGON32_LOGON_BATCH, LOGON32_PROVIDER_DEFAULT), the password in
    //    Marshal.StringToHGlobalUni, zeroed in a finally; BATCH, since a service logon adds SERVICE with
    //    SeImpersonatePrivilege. Failure: the worker account {account} of the base {key} cannot log on: {the Win32
    //    message}, which is also the reason when the token's groups or privileges cannot be read;
    // 2. the token's groups, whatever their attributes, and privileges, enabled or not, judged by WorkerAccountRule:
    //    the worker account {account} of the base {key} is an administrator or holds a privilege beyond an ordinary
    //    user's; expected a local user that is not in Administrators, as the console's install prepares it;
    // 3. the agent's token holds SeAssignPrimaryTokenPrivilege and SeIncreaseQuotaPrivilege, enabled or not, since
    //    CreateProcessAsUserW enables them: the agent's account lacks the right "Replace a process level token" or
    //    "Adjust memory quotas for a process"; install the agent with the console. When the agent's privileges cannot
    //    be read: the privileges of the agent's account cannot be read: {the Win32 message}.
    // The logon comes first, so that a missing account gives 1326 whatever the agent holds. The token lives as long as
    // the launcher; one logon per start of the agent, so no retries lock the account out.
    public void LogOn(string baseKey, string account, string password)
    {
        SafeAccessTokenHandle token = LogOnUser(baseKey, account, password);

        try
        {
            bool ordinary;

            try
            {
                ordinary = WorkerAccountRule.IsOrdinary(Groups(token), Privileges(token));
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
                List<string> agentPrivileges = Privileges(_agentToken);

                agentMayStart = agentPrivileges.Contains(AssignPrimaryToken) && agentPrivileges.Contains(IncreaseQuota);
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

            _tokens.Add(baseKey, token);
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
    public void Use(string baseKey, SafeAccessTokenHandle token, WorkerDesktop desktop)
    {
        throw new NotSupportedException();
    }

    // Closes the tokens of the logged-on bases, their desktops and the agent's own token.
    public void Dispose()
    {
        foreach (SafeAccessTokenHandle token in _tokens.Values)
        {
            token.Dispose();
        }

        _agentToken.Dispose();
    }

    // Reads the registry at each start (concept §5, O8): the x64 program when the connector is registered for 64 bits,
    // otherwise the x86 program when it is for 32 bits, otherwise a NoProcess. Two anonymous pipes, then
    // CreateProcessAsUserW with the token of the base's worker account when it was logged on, else the agent's own:
    // the program's full path, the command line "{program}" {base key} {read|write}, suspended and without a window,
    // the environment of the token's account, CreateEnvironmentBlock(token, false), never the agent's, the program's
    // folder as its working folder, lpDesktop "", which connects it to the window station of its logon session, the
    // client ends of the pipes as its standard input and output, a null standard error, and a list of the handles it
    // inherits that names those two ends alone. Then the agent's copies of the client ends close, the process joins the
    // job and its thread resumes, so that a worker is in the job before any of its code runs; the job lets nothing
    // break away from it. The connection with its password travels only in the first input line, never on the command
    // line or in the environment. Any failure, a created process terminated first, gives a NoProcess: Start throws
    // nothing but the ObjectDisposedException of a job that was disposed, which only a test does. Limitations: the profile of a worker account is not loaded, since only an administrator or LocalSystem
    // may load one, so its HKCU is the default hive and its TEMP the system's; and a worker that does not start keeps
    // no diagnostics.
    public IWorkerProcess Start(string baseKey, OutpostModeEnum mode)
    {
        string? program = ProgramOfConnector();

        if (program is null)
        {
            return new NoProcess();
        }

        SafeAccessTokenHandle token = _tokens.GetValueOrDefault(baseKey) ?? _agentToken;
        string commandLine = $"\"{program}\" {baseKey} {(mode == OutpostModeEnum.Write ? "write" : "read")}";

        AnonymousPipeServerStream? input = null;
        AnonymousPipeServerStream? output = null;

        try
        {
            input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

            (SafeProcessHandle Process, nint Thread)? created = CreateSuspended(
                token,
                program,
                commandLine,
                input.ClientSafePipeHandle,
                output.ClientSafePipeHandle);

            if (created is (SafeProcessHandle process, nint thread))
            {
                // The worker holds the client ends now: once the agent's copies close, its end closes its output.
                input.DisposeLocalCopyOfClientHandle();
                output.DisposeLocalCopyOfClientHandle();

                return JoinAndResume(new ComWorkerProcess(process, input, output), process, thread);
            }
        }
        catch (IOException)
        {
            // A pipe that Windows could not make.
        }

        input?.Dispose();
        output?.Dispose();

        return new NoProcess();
    }

    // The logon of step 1 of LogOn: the token, or an AgentStopException with the message of Windows.
    private static SafeAccessTokenHandle LogOnUser(string baseKey, string account, string password)
    {
        nint secret = Marshal.StringToHGlobalUni(password);
        SafeAccessTokenHandle token;
        bool loggedOn;
        int error;

        try
        {
            loggedOn = LogonUserW(account, ".", secret, BatchLogon, DefaultProvider, out token);
            error = Marshal.GetLastPInvokeError();
        }
        finally
        {
            // The native copy of the password is cleared before its memory is freed.
            Marshal.ZeroFreeGlobalAllocUnicode(secret);
        }

        if (!loggedOn)
        {
            token.Dispose();

            throw CannotLogOn(baseKey, account, error);
        }

        return token;
    }

    // The message of Windows for its error code holds the code's meaning alone, never a value given to the call.
    private static AgentStopException CannotLogOn(string baseKey, string account, int error)
    {
        return new AgentStopException(
            $"the worker account {account} of the base {baseKey} cannot log on: {new Win32Exception(error).Message}");
    }

    // The SIDs of the token's groups, whatever their attributes, deny-only ones included. Win32Exception when the token
    // cannot give them.
    private static unsafe List<string> Groups(SafeAccessTokenHandle token)
    {
        nint information = TokenInformation(token, TokenGroupsClass);

        try
        {
            // TOKEN_GROUPS: the count, then the entries at the alignment of their first member, a pointer.
            uint count = *(uint*)information;
            SidAndAttributes* entries = (SidAndAttributes*)(information + sizeof(nint));
            List<string> groups = [];

            for (uint index = 0; index < count; index++)
            {
                groups.Add(new SecurityIdentifier(entries[index].Sid).Value);
            }

            return groups;
        }
        finally
        {
            Marshal.FreeHGlobal(information);
        }
    }

    // The names of the token's privileges, enabled or not. Win32Exception when the token cannot give them.
    private static unsafe List<string> Privileges(SafeAccessTokenHandle token)
    {
        nint information = TokenInformation(token, TokenPrivilegesClass);

        try
        {
            // TOKEN_PRIVILEGES: the count, then the entries at the alignment of a 32-bit integer.
            uint count = *(uint*)information;
            LuidAndAttributes* entries = (LuidAndAttributes*)(information + sizeof(uint));
            List<string> privileges = [];

            for (uint index = 0; index < count; index++)
            {
                privileges.Add(PrivilegeName(entries[index].Luid));
            }

            return privileges;
        }
        finally
        {
            Marshal.FreeHGlobal(information);
        }
    }

    // The information of a class of the token, in native memory that the caller frees with Marshal.FreeHGlobal; its
    // entries point into that memory. Win32Exception when the token cannot give it.
    private static nint TokenInformation(SafeAccessTokenHandle token, int informationClass)
    {
        // The first call gives the size and fails, with ERROR_INSUFFICIENT_BUFFER.
        GetTokenInformation(token, informationClass, 0, 0, out int length);

        nint information = Marshal.AllocHGlobal(length);

        if (!GetTokenInformation(token, informationClass, information, length, out _))
        {
            int error = Marshal.GetLastPInvokeError();

            Marshal.FreeHGlobal(information);

            throw new Win32Exception(error);
        }

        return information;
    }

    // The name of a privilege, such as SeChangeNotifyPrivilege. Win32Exception when Windows does not know it.
    private static unsafe string PrivilegeName(Luid luid)
    {
        // Longer than the name of any privilege of Windows, with its null.
        const int Capacity = 128;

        char* name = stackalloc char[Capacity];
        int length = Capacity;

        if (!LookupPrivilegeNameW(null, &luid, name, ref length))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return new string(name, 0, length);
    }

    // CreateProcessAsUserW as Start says, with the environment block and the list of attributes that only this call
    // uses; null when a call fails, and then no process was created. The caller owns the handles it is given.
    private static unsafe (SafeProcessHandle Process, nint Thread)? CreateSuspended(
        SafeAccessTokenHandle token,
        string program,
        string commandLine,
        SafePipeHandle standardInput,
        SafePipeHandle standardOutput)
    {
        if (!CreateEnvironmentBlock(out nint environment, token, inherit: false))
        {
            return null;
        }

        // The first call gives the size of a list of one attribute and fails, with ERROR_INSUFFICIENT_BUFFER.
        nuint listSize = 0;
        InitializeProcThreadAttributeList(0, 1, 0, ref listSize);

        nint attributes = Marshal.AllocHGlobal((nint)listSize);
        bool initialized = false;

        try
        {
            initialized = InitializeProcThreadAttributeList(attributes, 1, 0, ref listSize);

            // The list points at the handles, which stay on this stack until CreateProcessAsUserW returned.
            nint* inherited = stackalloc nint[2];
            inherited[0] = standardInput.DangerousGetHandle();
            inherited[1] = standardOutput.DangerousGetHandle();

            if (!initialized
                || !UpdateProcThreadAttribute(attributes, 0, HandleListAttribute, (nint)inherited, (nuint)(2 * sizeof(nint)), 0, 0))
            {
                return null;
            }

            // CreateProcessAsUserW may write into the command line, so it gets a copy of its own, ended by a null.
            char[] line = [.. commandLine, '\0'];

            fixed (char* desktop = "")
            fixed (char* lineStart = line)
            {
                StartupInfo startup = new()
                {
                    Size = Marshal.SizeOf<StartupInfo>(),
                    Desktop = (nint)desktop,
                    Flags = UseStandardHandles,
                    StandardInput = inherited[0],
                    StandardOutput = inherited[1],
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
                    return null;
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

    // Puts the suspended worker into the job and resumes its thread, whose handle it closes. A worker that the job does
    // not take, which would outlive the agent, or whose thread does not resume is killed and disposed: a NoProcess. Any
    // other exception, such as the ObjectDisposedException of a disposed job, passes after the kill as well, so that no
    // process is ever left outside the job.
    private IWorkerProcess JoinAndResume(ComWorkerProcess worker, SafeProcessHandle process, nint thread)
    {
        bool resumed = false;

        try
        {
            _job.Add(process);
            resumed = ResumeThread(thread) != ResumeFailed;
        }
        catch (Win32Exception)
        {
            // Not in the job, so never resumed.
        }
        finally
        {
            CloseHandle(thread);

            if (!resumed)
            {
                worker.Kill();
                worker.Dispose();
            }
        }

        if (resumed)
        {
            return worker;
        }

        return new NoProcess();
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

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);

    // The password is a pointer to native memory that the caller clears, never a string that the marshalling copies.
    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LogonUserW(
        string user,
        string domain,
        nint password,
        int logonType,
        int logonProvider,
        out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(
        SafeAccessTokenHandle token,
        int informationClass,
        nint information,
        int length,
        out int returnLength);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool LookupPrivilegeNameW(string? systemName, Luid* luid, char* name, ref int length);

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
}
