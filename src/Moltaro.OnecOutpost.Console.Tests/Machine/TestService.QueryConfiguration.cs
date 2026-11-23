using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

internal sealed partial class TestService
{
    // QUERY_SERVICE_CONFIGW, whose strings lie in the buffer that QueryServiceConfigW filled after it.
    [StructLayout(LayoutKind.Sequential)]
    private struct QueryConfiguration
    {
        public uint ServiceType;

        public uint StartType;

        public uint ErrorControl;

        public nint BinaryPathName;

        public nint LoadOrderGroup;

        public uint TagId;

        public nint Dependencies;

        public nint ServiceStartName;

        public nint DisplayName;
    }
}
