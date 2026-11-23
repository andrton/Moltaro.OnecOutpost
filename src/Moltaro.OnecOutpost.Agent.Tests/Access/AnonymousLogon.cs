using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Agent.Tests.Access;

// Runs a function on the calling thread while the thread impersonates ANONYMOUS LOGON, S-1-5-7, and reverts the thread
// to its own account before it returns. WindowsIdentity.GetCurrent() then gives ANONYMOUS LOGON, so access lists made
// in the function take it for the agent's account, and the account that runs the tests, which owns every entry that a
// test makes, for somebody else: no test can make an entry that another account owns, which takes SeRestorePrivilege.
// The function must not wait, so that it ends on the thread that impersonates.
[SupportedOSPlatform("windows")]
internal static partial class AnonymousLogon
{
    public static T Run<T>(Func<T> function)
    {
        if (!ImpersonateAnonymousToken(GetCurrentThread()))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            return function();
        }
        finally
        {
            if (!RevertToSelf())
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentThread();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ImpersonateAnonymousToken(IntPtr thread);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RevertToSelf();
}
