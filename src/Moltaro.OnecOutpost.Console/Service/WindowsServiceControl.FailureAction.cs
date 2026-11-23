using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Service;

internal sealed partial class WindowsServiceControl
{
    // SC_ACTION of winsvc.h: what Windows does after a failure of the service, and after how many milliseconds.
    [StructLayout(LayoutKind.Sequential)]
    private struct FailureAction
    {
        public int Type;

        public uint Delay;
    }
}
