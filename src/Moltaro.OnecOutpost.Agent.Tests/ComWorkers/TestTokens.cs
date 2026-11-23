using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// Tokens of the process of the tests, which stands for the agent, for the tests of the worker's desktop: its own
// primary token, as the launcher opens the agent's; its logon SID; and a restricted version of it that holds some of
// its groups for deny only, which a process may start a child with as with its own token, and which an allow entry for
// those groups does not admit.
[SupportedOSPlatform("windows")]
internal static partial class TestTokens
{
    // TOKEN_ASSIGN_PRIMARY, TOKEN_DUPLICATE and TOKEN_QUERY, as the launcher opens the agent's own token.
    private const uint TokenAccess = 0x0001 | 0x0002 | 0x0008;

    // TokenGroups of TOKEN_INFORMATION_CLASS, and SE_GROUP_LOGON_ID.
    private const int TokenGroupsClass = 2;

    private const uint LogonId = 0xC0000000;

    // ERROR_INSUFFICIENT_BUFFER, of the first call that asks for the size of the groups.
    private const int InsufficientBuffer = 122;

    public static SafeAccessTokenHandle Own()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAccess, out SafeAccessTokenHandle token))
        {
            int error = Marshal.GetLastPInvokeError();

            token.Dispose();

            throw new Win32Exception(error);
        }

        return token;
    }

    // The group of the token with the attribute SE_GROUP_LOGON_ID.
    public static unsafe SecurityIdentifier LogonSidOf(SafeAccessTokenHandle token)
    {
        if (!GetTokenInformation(token, TokenGroupsClass, null, 0, out int length)
            && Marshal.GetLastPInvokeError() != InsufficientBuffer)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        byte[] groups = new byte[length];

        fixed (byte* start = groups)
        {
            if (!GetTokenInformation(token, TokenGroupsClass, start, length, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            // TOKEN_GROUPS: GroupCount, then its SID_AND_ATTRIBUTES from the next pointer-aligned byte, each a pointer
            // to the SID and the attributes, two pointers wide.
            uint count = *(uint*)start;
            nint* entries = (nint*)(start + sizeof(nint));

            for (int index = 0; index < count; index++)
            {
                uint attributes = (uint)entries[(2 * index) + 1];

                if ((attributes & LogonId) == LogonId)
                {
                    return new SecurityIdentifier(entries[2 * index]);
                }
            }
        }

        throw new InvalidOperationException("The token holds no logon SID.");
    }

    // CreateRestrictedToken with these SIDs to disable, so that the new token holds them for deny only.
    public static unsafe SafeAccessTokenHandle WithDenyOnly(SafeAccessTokenHandle token, params SecurityIdentifier[] sids)
    {
        nint[] binary = new nint[sids.Length];

        try
        {
            // SID_AND_ATTRIBUTES: the pointer to the SID and the attributes, none here, two pointers wide.
            nint* entries = stackalloc nint[2 * sids.Length];

            for (int index = 0; index < sids.Length; index++)
            {
                byte[] form = new byte[sids[index].BinaryLength];
                sids[index].GetBinaryForm(form, 0);

                binary[index] = Marshal.AllocHGlobal(form.Length);
                Marshal.Copy(form, 0, binary[index], form.Length);

                entries[2 * index] = binary[index];
                entries[(2 * index) + 1] = 0;
            }

            if (!CreateRestrictedToken(token, 0, (uint)sids.Length, entries, 0, null, 0, null, out SafeAccessTokenHandle restricted))
            {
                int error = Marshal.GetLastPInvokeError();

                restricted.Dispose();

                throw new Win32Exception(error);
            }

            return restricted;
        }
        finally
        {
            foreach (nint sid in binary)
            {
                Marshal.FreeHGlobal(sid);
            }
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetTokenInformation(
        SafeAccessTokenHandle token,
        int informationClass,
        void* information,
        int length,
        out int returnLength);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CreateRestrictedToken(
        SafeAccessTokenHandle existing,
        uint flags,
        uint disableSidCount,
        void* sidsToDisable,
        uint deletePrivilegeCount,
        void* privilegesToDelete,
        uint restrictedSidCount,
        void* sidsToRestrict,
        out SafeAccessTokenHandle restricted);
}
