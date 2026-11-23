using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.OneC.Tests.Lab;

namespace Moltaro.OnecOutpost.OneC.Tests.Agent;

// The logon of a worker account by the tests themselves, for a batch job as the launcher logs it on: for the probe of
// a worker that did not open, and for the wait for the end of the workers of an agent, which only their account may
// open. The tests logged the account on with its password, so they may impersonate it. The password travels only to
// LogonUserW, in native memory cleared after the call.
[SupportedOSPlatform("windows")]
internal static partial class LabLogon
{
    // LOGON32_LOGON_BATCH and LOGON32_PROVIDER_DEFAULT.
    private const int BatchLogon = 4;

    private const int DefaultProvider = 0;

    // The token of the account, or null with the Win32 error of the logon.
    public static SafeAccessTokenHandle? LogOn(LabAccount account, out int error)
    {
        nint secret = Marshal.StringToHGlobalUni(account.Password);
        bool loggedOn;
        SafeAccessTokenHandle token;

        try
        {
            loggedOn = LogonUserW(account.User, ".", secret, BatchLogon, DefaultProvider, out token);
            error = Marshal.GetLastPInvokeError();
        }
        finally
        {
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
