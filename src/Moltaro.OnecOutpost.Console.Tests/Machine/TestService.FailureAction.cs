using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

internal sealed partial class TestService
{
    // SC_ACTION: what the service controller does after a failure, and after how many milliseconds.
    [StructLayout(LayoutKind.Sequential)]
    private struct FailureAction
    {
        public int Type;

        public uint Delay;
    }
}
