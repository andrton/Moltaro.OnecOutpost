using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Agent.Workers;

internal static partial class WorkerToken
{
    // TOKEN_STATISTICS of winnt.h, which GetTokenInformation gives: only AuthenticationId, the identifier of the token's
    // logon session, is read. The sequential layout aligns each field as the C compiler does, for 64 and for 32 bits.
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenStatistics
    {
        public Luid TokenId;

        public Luid AuthenticationId;

        public long ExpirationTime;

        public int TokenType;

        public int ImpersonationLevel;

        public uint DynamicCharged;

        public uint DynamicAvailable;

        public uint GroupCount;

        public uint PrivilegeCount;

        public Luid ModifiedId;
    }
}
