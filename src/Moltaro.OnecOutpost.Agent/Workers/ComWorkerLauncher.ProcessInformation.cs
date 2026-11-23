using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class ComWorkerLauncher
{
    // PROCESS_INFORMATION of processthreadsapi.h: the handles of the created process and of its first thread, which the
    // caller owns, and their identities.
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;

        public nint Thread;

        public int ProcessId;

        public int ThreadId;
    }
}
