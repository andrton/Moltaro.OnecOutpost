using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Workers;

internal sealed partial class WindowsWorkerStarts
{
    // SID_AND_ATTRIBUTES of winnt.h, in its order: a pointer to a SID that the caller keeps pinned, and its attributes,
    // which CreateRestrictedToken ignores for the SIDs to disable; TOKEN_MANDATORY_LABEL is one of them.
    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public nint Sid;

        public uint Attributes;
    }
}
