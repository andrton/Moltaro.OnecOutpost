using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class ComWorkerLauncher
{
    // SID_AND_ATTRIBUTES of winnt.h, an entry of the TOKEN_GROUPS that GetTokenInformation gives: a pointer to the SID
    // in the same buffer, and its attributes.
    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public nint Sid;

        public uint Attributes;
    }
}
