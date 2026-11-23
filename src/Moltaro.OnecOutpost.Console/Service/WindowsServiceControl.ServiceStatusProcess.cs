using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Service;

internal sealed partial class WindowsServiceControl
{
    // SERVICE_STATUS_PROCESS of winsvc.h: the state of the service and the milliseconds it expects its stop to take.
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;

        public uint CurrentState;

        public uint ControlsAccepted;

        public uint Win32ExitCode;

        public uint ServiceSpecificExitCode;

        public uint CheckPoint;

        public uint WaitHint;

        public uint ProcessId;

        public uint ServiceFlags;
    }
}
