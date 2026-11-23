using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.OneC.Tests.Agent;

internal static partial class LabWorkerProbe
{
    // PROCESS_INFORMATION of processthreadsapi.h: the handles of the created process and of its first thread, which the
    // probe closes, and their identities.
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;

        public nint Thread;

        public int ProcessId;

        public int ThreadId;
    }
}
