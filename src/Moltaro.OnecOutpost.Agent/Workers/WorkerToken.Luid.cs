using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Agent.Workers;

internal static partial class WorkerToken
{
    // LUID of winnt.h, the identity of a privilege on this machine: two 32-bit parts, so aligned as a 32-bit integer,
    // never as a 64-bit one.
    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;

        public int HighPart;
    }
}
