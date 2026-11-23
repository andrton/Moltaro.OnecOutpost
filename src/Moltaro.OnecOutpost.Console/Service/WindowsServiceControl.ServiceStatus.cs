using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Service;

internal sealed partial class WindowsServiceControl
{
    // SERVICE_STATUS of winsvc.h, which ControlService fills; the console reads its state by QueryServiceStatusEx.
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;

        public uint CurrentState;

        public uint ControlsAccepted;

        public uint Win32ExitCode;

        public uint ServiceSpecificExitCode;

        public uint CheckPoint;

        public uint WaitHint;
    }
}
