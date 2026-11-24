using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The logon of a worker account and the judgment of its token, for the agent's launcher and for the console's check.
// Every call to Windows goes through LibraryImport, no package (principle 12).
[SupportedOSPlatform("windows")]
internal static partial class WorkerLogon
{
    // LOGON32_LOGON_BATCH and LOGON32_PROVIDER_DEFAULT.
    private const int BatchLogon = 4;

    private const int DefaultProvider = 0;

    // LogonUserW(account, ".", password, LOGON32_LOGON_BATCH, LOGON32_PROVIDER_DEFAULT): the token, or null with the
    // Win32 error of the logon. BATCH, since a service logon adds SERVICE with SeImpersonatePrivilege; its batch right is
    // judged by the logon itself, so a right through a group and a deny entry count. The password reaches only the call,
    // from native memory that is cleared after it, and nothing else holds it. One call per logon, never retried, since
    // each failure counts toward a lockout.
    public static SafeAccessTokenHandle? LogOn(string account, string password, out int error)
    {
        nint secret = Marshal.StringToHGlobalUni(password);
        SafeAccessTokenHandle token;
        bool loggedOn;

        try
        {
            loggedOn = LogonUserW(account, ".", secret, BatchLogon, DefaultProvider, out token);
            error = Marshal.GetLastPInvokeError();
        }
        finally
        {
            // The native copy of the password is cleared before its memory is freed.
            Marshal.ZeroFreeGlobalAllocUnicode(secret);
        }

        if (!loggedOn)
        {
            token.Dispose();

            return null;
        }

        error = 0;

        return token;
    }

    // WorkerAccountRule over the token's groups, whatever their attributes, and its privileges, enabled or not. A token
    // whose groups or privileges cannot be read is a Win32Exception.
    public static bool IsOrdinary(SafeAccessTokenHandle token)
    {
        List<string> groups = [.. WorkerToken.Groups(token).Select(group => group.Sid.Value)];

        return WorkerAccountRule.IsOrdinary(groups, WorkerToken.Privileges(token));
    }

    // The password is a pointer to native memory that the caller clears, never a string that the marshalling copies.
    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LogonUserW(
        string user,
        string domain,
        nint password,
        int logonType,
        int logonProvider,
        out SafeAccessTokenHandle token);
}
