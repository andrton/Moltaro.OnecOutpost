using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

internal sealed partial class TestService
{
    // SERVICE_FAILURE_ACTIONSW: the period that resets the count of failures, in seconds, no reboot message and no
    // command, and the actions.
    [StructLayout(LayoutKind.Sequential)]
    private struct FailureActionList
    {
        public uint ResetPeriod;

        public nint RebootMessage;

        public nint Command;

        public uint ActionCount;

        public nint Actions;
    }
}
