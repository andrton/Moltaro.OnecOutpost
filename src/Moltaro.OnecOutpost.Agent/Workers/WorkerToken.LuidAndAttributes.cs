using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Agent.Workers;

internal static partial class WorkerToken
{
    // LUID_AND_ATTRIBUTES of winnt.h, an entry of the TOKEN_PRIVILEGES that GetTokenInformation gives: the privilege and
    // whether it is enabled.
    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;

        public uint Attributes;
    }
}
