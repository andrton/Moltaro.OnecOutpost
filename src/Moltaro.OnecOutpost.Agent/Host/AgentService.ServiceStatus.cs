using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Agent.Host;

internal static partial class AgentService
{
    // SERVICE_STATUS of winsvc.h: what the service reports to the controller.
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public int ServiceType;

        public int CurrentState;

        public int ControlsAccepted;

        public int Win32ExitCode;

        public int ServiceSpecificExitCode;

        public int CheckPoint;

        public int WaitHint;
    }
}
