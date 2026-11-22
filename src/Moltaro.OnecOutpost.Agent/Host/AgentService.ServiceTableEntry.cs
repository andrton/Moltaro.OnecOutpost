using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Agent.Host;

internal static unsafe partial class AgentService
{
    // SERVICE_TABLE_ENTRYW of winsvc.h: the name of a service of the process and its ServiceMain.
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceTableEntry
    {
        public char* Name;

        public delegate* unmanaged<int, char**, void> Main;
    }
}
