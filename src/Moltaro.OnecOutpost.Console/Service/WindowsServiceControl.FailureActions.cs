using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Service;

internal sealed partial class WindowsServiceControl
{
    // SERVICE_FAILURE_ACTIONSW of winsvc.h: the seconds after which the count of failures starts again, no message of
    // a reboot and no command, and the actions, one for each failure counted, the last repeated.
    [StructLayout(LayoutKind.Sequential)]
    private struct FailureActions
    {
        public uint ResetPeriod;

        public nint RebootMessage;

        public nint Command;

        public uint ActionCount;

        public nint Actions;
    }
}
