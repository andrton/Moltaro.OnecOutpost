using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Agent.Host;

// The Windows service: the dispatcher, the handler and SetServiceStatus through LibraryImport, the entry points static
// and [UnmanagedCallersOnly], catching everything, so no delegate lives. ServiceMain reports SERVICE_RUNNING, accepting
// stop and shutdown, and runs the run on its thread; stop and shutdown report STOP_PENDING and cancel from the thread
// pool; SERVICE_STOPPED comes last, on every stop, with 0 or ERROR_SERVICE_SPECIFIC_ERROR and the code, so that the
// failure actions, which restart only a process that ends without reporting, leave a deliberate stop with an error
// stopped. The wait hint of STOP_PENDING is no wait of the agent: no option.
//
// The entry points reach the run, its token and the status handle through the fields below, since a function pointer
// carries no state. Run sets the run before the dispatcher starts; ServiceMain, on the dispatcher's thread for the
// service, sets the handle and the exit code; the handler, on the dispatcher's main thread, only reads the handle and
// cancels. The dispatcher starts ServiceMain and the handler, so each sees what was set before.
[SupportedOSPlatform("windows")]
internal static unsafe partial class AgentService
{
    private const int ServiceWin32OwnProcess = 0x10;

    private const int ServiceStopped = 1;

    private const int ServiceStopPending = 3;

    private const int ServiceRunning = 4;

    private const int ServiceAcceptStop = 0x1;

    private const int ServiceAcceptShutdown = 0x4;

    private const int ServiceControlStop = 1;

    private const int ServiceControlInterrogate = 4;

    private const int ServiceControlShutdown = 5;

    private const int NoError = 0;

    private const int ErrorCallNotImplemented = 120;

    private const int ErrorFailedServiceControllerConnect = 1063;

    private const int ErrorServiceSpecificError = 1066;

    // A hint to the service controller of how long a stop takes; the agent waits for nothing by it.
    private const int StopWaitHintMs = 30_000;

    private static readonly CancellationTokenSource Stop = new();

    private static Func<CancellationToken, Task<int>>? _run;

    private static nint _status;

    private static int _exitCode;

    // Runs run as the service and gives its exit code. Error 1063, a process that the service controller did not
    // start, gives NotService before the data folder is touched.
    public static int Run(Func<CancellationToken, Task<int>> run)
    {
        _run = run;

        // One service in its own process: the controller checks no name, so it is empty. The table ends with an entry
        // that is all zero.
        char* name = stackalloc char[1];
        name[0] = '\0';

        ServiceTableEntry* table = stackalloc ServiceTableEntry[2];
        table[0] = new ServiceTableEntry { Name = name, Main = &ServiceMain };
        table[1] = default;

        // Returns once the service has stopped; the process may end at any moment after SERVICE_STOPPED.
        if (StartServiceCtrlDispatcher(table))
        {
            return _exitCode;
        }

        return Marshal.GetLastPInvokeError() == ErrorFailedServiceControllerConnect
            ? AgentExitCodes.NotService
            : AgentExitCodes.Error;
    }

    [UnmanagedCallersOnly]
    private static void ServiceMain(int argumentCount, char** arguments)
    {
        int code;

        try
        {
            _status = RegisterServiceCtrlHandlerEx("", &Handler, 0);
            Report(ServiceRunning, ServiceAcceptStop | ServiceAcceptShutdown, 0);

            code = _run!(Stop.Token).GetAwaiter().GetResult();
        }
        catch
        {
            // An exception that escaped here would end the process before it reports its stop.
            code = AgentExitCodes.Error;
        }

        _exitCode = code;

        // Last: after SERVICE_STOPPED the controller may end the process at any moment.
        Report(ServiceStopped, 0, code);
    }

    [UnmanagedCallersOnly]
    private static int Handler(int control, int eventType, nint eventData, nint context)
    {
        try
        {
            switch (control)
            {
                case ServiceControlStop:
                case ServiceControlShutdown:
                    Report(ServiceStopPending, 0, 0);

                    // The callbacks of the token run on the thread pool, never on the dispatcher's thread.
                    _ = Stop.CancelAsync();

                    return NoError;

                case ServiceControlInterrogate:
                    return NoError;

                default:
                    return ErrorCallNotImplemented;
            }
        }
        catch
        {
            return NoError;
        }
    }

    private static void Report(int state, int accepted, int exitCode)
    {
        ServiceStatus status = new()
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = state,
            ControlsAccepted = accepted,
            Win32ExitCode = exitCode == 0 ? NoError : ErrorServiceSpecificError,
            ServiceSpecificExitCode = exitCode,
            WaitHint = state == ServiceStopPending ? StopWaitHintMs : 0,
        };

        SetServiceStatus(_status, &status);
    }

    [LibraryImport("advapi32.dll", EntryPoint = "StartServiceCtrlDispatcherW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartServiceCtrlDispatcher(ServiceTableEntry* table);

    // The handle it gives is the controller's, never closed.
    [LibraryImport(
        "advapi32.dll",
        EntryPoint = "RegisterServiceCtrlHandlerExW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint RegisterServiceCtrlHandlerEx(
        string name,
        delegate* unmanaged<int, int, nint, nint, int> handler,
        nint context);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetServiceStatus(nint status, ServiceStatus* serviceStatus);
}
