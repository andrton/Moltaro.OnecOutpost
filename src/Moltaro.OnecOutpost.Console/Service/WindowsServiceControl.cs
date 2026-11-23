using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Console.Service;

// The service controller of Windows through LibraryImport, its service handle with SERVICE_START, which restart actions
// need. Install: ChangeServiceConfigW of an existing service, keeping its failure actions; else CreateServiceW, own
// process, automatic start, NT SERVICE\{name}, no password, with the failure actions: restart after 1 minute, three
// times, the count reset after a day, the flag for a stop with an error left false, as a new service has it, since
// AgentService stops so on its own. Both set the description and no required privileges, which would drop the agent's
// two. StopAsync: 1060 is NotInstalled; SERVICE_CONTROL_STOP, 1062 counting as stopped and 1061 waiting;
// QueryServiceStatusEx every tenth of the wait hint, 1 to 10 seconds, until stopped or the timeout by the clock. Delete:
// 1072 counts as done. Every handle is closed by its owner, once.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsServiceControl : IServiceControl
{
    private const string Description =
        "The agent of Moltaro.OnecOutpost: it dials out to its server and runs in the 1C:Enterprise bases of this machine "
        + "the commands that the server signed and the local policy allows.";

    // SC_MANAGER_CONNECT and SC_MANAGER_CREATE_SERVICE.
    private const uint ManagerConnect = 0x0001;

    private const uint ManagerCreateService = 0x0002;

    // SERVICE_CHANGE_CONFIG, SERVICE_QUERY_STATUS, SERVICE_START, SERVICE_STOP and DELETE.
    private const uint ChangeConfig = 0x0002;

    private const uint QueryStatus = 0x0004;

    private const uint StartService = 0x0010;

    private const uint StopService = 0x0020;

    private const uint DeleteAccess = 0x00010000;

    // SERVICE_WIN32_OWN_PROCESS, SERVICE_AUTO_START and SERVICE_ERROR_NORMAL.
    private const uint OwnProcess = 0x10;

    private const uint AutomaticStart = 2;

    private const uint ErrorNormal = 1;

    // SERVICE_CONTROL_STOP, SERVICE_STOPPED and SC_STATUS_PROCESS_INFO.
    private const uint ControlStop = 1;

    private const uint StoppedState = 1;

    private const uint StatusProcessInfo = 0;

    // SERVICE_CONFIG_DESCRIPTION, SERVICE_CONFIG_FAILURE_ACTIONS and SC_ACTION_RESTART.
    private const uint DescriptionLevel = 1;

    private const uint FailureActionsLevel = 2;

    private const int RestartAction = 1;

    // ERROR_SERVICE_DOES_NOT_EXIST, ERROR_SERVICE_CANNOT_ACCEPT_CTRL, ERROR_SERVICE_NOT_ACTIVE and
    // ERROR_SERVICE_MARKED_FOR_DELETE.
    private const int ServiceDoesNotExist = 1060;

    private const int CannotAcceptControl = 1061;

    private const int ServiceNotActive = 1062;

    private const int MarkedForDeletion = 1072;

    // The failure actions of a service that the console creates: Windows restarts a service whose process ended
    // without reporting that it stopped, after a minute, three times, and counts the failures again after a day.
    private const int Restarts = 3;

    private static readonly TimeSpan RestartDelay = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan FailureCountReset = TimeSpan.FromDays(1);

    // The bounds of the wait between two queries of a stopping service, a tenth of its wait hint.
    private static readonly TimeSpan ShortestQueryDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan LongestQueryDelay = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _clock;

    // The clock of the wait for a stop.
    public WindowsServiceControl(TimeProvider clock)
    {
        _clock = clock;
    }

    public bool Install(string name, string displayName, string binaryPath)
    {
        string account = $@"NT SERVICE\{name}";

        using ServiceHandle manager = OpenManager(ManagerConnect | ManagerCreateService);
        ServiceHandle service = OpenServiceW(manager, name, ChangeConfig | StartService);

        try
        {
            bool created = false;

            if (service.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();

                if (error != ServiceDoesNotExist)
                {
                    throw new Win32Exception(error);
                }

                service.Dispose();
                service = CreateServiceW(
                    manager,
                    name,
                    displayName,
                    ChangeConfig | StartService,
                    OwnProcess,
                    AutomaticStart,
                    ErrorNormal,
                    binaryPath,
                    null,
                    0,
                    null,
                    account,
                    null);

                if (service.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                created = true;
                SetFailureActions(service);
            }
            else
            {
                // An existing service is never deleted, so its failure actions, as an administrator may have set them,
                // stay, and no deletion is ever pending for install to meet.
                Check(ChangeServiceConfigW(
                    service,
                    OwnProcess,
                    AutomaticStart,
                    ErrorNormal,
                    binaryPath,
                    null,
                    0,
                    null,
                    account,
                    null,
                    displayName));
            }

            SetDescription(service);

            return created;
        }
        finally
        {
            service.Dispose();
        }
    }

    public async Task<ServiceStopEnum> StopAsync(string name, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using ServiceHandle manager = OpenManager(ManagerConnect);
        using ServiceHandle service = OpenServiceW(manager, name, StopService | QueryStatus);

        if (service.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();

            if (error != ServiceDoesNotExist)
            {
                throw new Win32Exception(error);
            }

            return ServiceStopEnum.NotInstalled;
        }

        DateTimeOffset deadline = _clock.GetUtcNow() + timeout;

        if (!ControlService(service, ControlStop, out _))
        {
            int error = Marshal.GetLastPInvokeError();

            if (error == ServiceNotActive)
            {
                return ServiceStopEnum.Stopped;
            }

            // A service that is starting or already stopping cannot take the control now; it is waited for all the same.
            if (error != CannotAcceptControl)
            {
                throw new Win32Exception(error);
            }
        }

        while (true)
        {
            Check(QueryServiceStatusEx(service, StatusProcessInfo, out ServiceStatusProcess status, (uint)Marshal.SizeOf<ServiceStatusProcess>(), out _));

            if (status.CurrentState == StoppedState)
            {
                return ServiceStopEnum.Stopped;
            }

            TimeSpan left = deadline - _clock.GetUtcNow();

            if (left <= TimeSpan.Zero)
            {
                return ServiceStopEnum.TimedOut;
            }

            TimeSpan delay = TimeSpan.FromMilliseconds(Math.Clamp(
                status.WaitHint / 10.0,
                ShortestQueryDelay.TotalMilliseconds,
                LongestQueryDelay.TotalMilliseconds));

            await Task.Delay(delay < left ? delay : left, _clock, cancellationToken);
        }
    }

    public void Delete(string name)
    {
        using ServiceHandle manager = OpenManager(ManagerConnect);
        using ServiceHandle service = OpenServiceW(manager, name, DeleteAccess);

        // DeleteService only marks the service; Windows removes it once the last handle of it is closed.
        if (!service.IsInvalid && DeleteService(service))
        {
            return;
        }

        int error = Marshal.GetLastPInvokeError();

        if (error != MarkedForDeletion)
        {
            throw new Win32Exception(error);
        }
    }

    private static ServiceHandle OpenManager(uint access)
    {
        ServiceHandle manager = OpenSCManagerW(null, null, access);

        if (manager.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();

            manager.Dispose();

            throw new Win32Exception(error);
        }

        return manager;
    }

    // Restart actions need SERVICE_START on the handle.
    private static unsafe void SetFailureActions(ServiceHandle service)
    {
        FailureAction* actions = stackalloc FailureAction[Restarts];

        for (int index = 0; index < Restarts; index++)
        {
            actions[index] = new FailureAction { Type = RestartAction, Delay = (uint)RestartDelay.TotalMilliseconds };
        }

        FailureActions info = new()
        {
            ResetPeriod = (uint)FailureCountReset.TotalSeconds,
            ActionCount = Restarts,
            Actions = (nint)actions,
        };

        Check(ChangeServiceConfig2W(service, FailureActionsLevel, (nint)(&info)));
    }

    // Windows copies the text, which it only reads, so the pinned string serves.
    private static unsafe void SetDescription(ServiceHandle service)
    {
        fixed (char* text = Description)
        {
            ServiceDescription info = new() { Text = (nint)text };

            Check(ChangeServiceConfig2W(service, DescriptionLevel, (nint)(&info)));
        }
    }

    private static void Check(bool done)
    {
        if (!done)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial ServiceHandle OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial ServiceHandle OpenServiceW(ServiceHandle manager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial ServiceHandle CreateServiceW(
        ServiceHandle manager,
        string serviceName,
        string displayName,
        uint desiredAccess,
        uint serviceType,
        uint startType,
        uint errorControl,
        string binaryPathName,
        string? loadOrderGroup,
        nint tagId,
        string? dependencies,
        string serviceStartName,
        string? password);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeServiceConfigW(
        ServiceHandle service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string binaryPathName,
        string? loadOrderGroup,
        nint tagId,
        string? dependencies,
        string serviceStartName,
        string? password,
        string displayName);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeServiceConfig2W(ServiceHandle service, uint infoLevel, nint info);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ControlService(ServiceHandle service, uint control, out ServiceStatus status);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatusEx(
        ServiceHandle service,
        uint infoLevel,
        out ServiceStatusProcess status,
        uint size,
        out uint needed);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteService(ServiceHandle service);
}
