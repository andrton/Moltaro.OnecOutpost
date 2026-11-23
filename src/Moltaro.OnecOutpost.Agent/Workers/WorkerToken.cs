using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Agent.Workers;

// What the launcher and the desktop of the workers read of a token: its groups, its privileges and its logon session,
// through GetTokenInformation. Every call to Windows goes through LibraryImport.
[SupportedOSPlatform("windows")]
internal static partial class WorkerToken
{
    // TokenGroups, TokenPrivileges and TokenStatistics of TOKEN_INFORMATION_CLASS.
    private const int TokenGroupsClass = 2;

    private const int TokenPrivilegesClass = 3;

    private const int TokenStatisticsClass = 10;

    // The groups of the token, whatever their attributes, deny-only ones included, each with its attributes: the
    // launcher's LogOn judges their SIDs, and WorkerDesktop takes the logon SID from them. Win32Exception when the token
    // cannot give them.
    public static unsafe List<(SecurityIdentifier Sid, uint Attributes)> Groups(SafeAccessTokenHandle token)
    {
        nint information = TokenInformation(token, TokenGroupsClass);

        try
        {
            // TOKEN_GROUPS: the count, then the entries at the alignment of their first member, a pointer.
            uint count = *(uint*)information;
            SidAndAttributes* entries = (SidAndAttributes*)(information + sizeof(nint));
            List<(SecurityIdentifier Sid, uint Attributes)> groups = [];

            for (uint index = 0; index < count; index++)
            {
                groups.Add((new SecurityIdentifier(entries[index].Sid), entries[index].Attributes));
            }

            return groups;
        }
        finally
        {
            Marshal.FreeHGlobal(information);
        }
    }

    // The identifier of the token's logon session, its AuthenticationId, as one 64-bit number: WorkerDesktop names the
    // desktop of a logon after it. Win32Exception when the token cannot give it.
    public static unsafe ulong LogonOf(SafeAccessTokenHandle token)
    {
        nint information = TokenInformation(token, TokenStatisticsClass);

        try
        {
            Luid logon = ((TokenStatistics*)information)->AuthenticationId;

            return ((ulong)(uint)logon.HighPart << 32) | logon.LowPart;
        }
        finally
        {
            Marshal.FreeHGlobal(information);
        }
    }

    // The names of the token's privileges, enabled or not. Win32Exception when the token cannot give them.
    public static unsafe List<string> Privileges(SafeAccessTokenHandle token)
    {
        nint information = TokenInformation(token, TokenPrivilegesClass);

        try
        {
            // TOKEN_PRIVILEGES: the count, then the entries at the alignment of a 32-bit integer.
            uint count = *(uint*)information;
            LuidAndAttributes* entries = (LuidAndAttributes*)(information + sizeof(uint));
            List<string> privileges = [];

            for (uint index = 0; index < count; index++)
            {
                privileges.Add(PrivilegeName(entries[index].Luid));
            }

            return privileges;
        }
        finally
        {
            Marshal.FreeHGlobal(information);
        }
    }

    // The information of a class of the token, in native memory that the caller frees with Marshal.FreeHGlobal; its
    // entries point into that memory. Win32Exception when the token cannot give it.
    private static nint TokenInformation(SafeAccessTokenHandle token, int informationClass)
    {
        // The first call gives the size and fails, with ERROR_INSUFFICIENT_BUFFER.
        GetTokenInformation(token, informationClass, 0, 0, out int length);

        nint information = Marshal.AllocHGlobal(length);

        if (!GetTokenInformation(token, informationClass, information, length, out _))
        {
            int error = Marshal.GetLastPInvokeError();

            Marshal.FreeHGlobal(information);

            throw new Win32Exception(error);
        }

        return information;
    }

    // The name of a privilege, such as SeChangeNotifyPrivilege. Win32Exception when Windows does not know it.
    private static unsafe string PrivilegeName(Luid luid)
    {
        // Longer than the name of any privilege of Windows, with its null.
        const int Capacity = 128;

        char* name = stackalloc char[Capacity];
        int length = Capacity;

        if (!LookupPrivilegeNameW(null, &luid, name, ref length))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return new string(name, 0, length);
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(
        SafeAccessTokenHandle token,
        int informationClass,
        nint information,
        int length,
        out int returnLength);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool LookupPrivilegeNameW(string? systemName, Luid* luid, char* name, ref int length);
}
