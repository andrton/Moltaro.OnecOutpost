using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Console.Rights;

internal sealed partial class WindowsAccountRights
{
    // A handle of the policy of LSA, closed once with LsaClose. The marshalling of LibraryImport makes it with the
    // parameterless constructor.
    private sealed partial class PolicyHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public PolicyHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return LsaClose(handle) == 0;
        }

        [LibraryImport("advapi32.dll")]
        private static partial uint LsaClose(nint handle);
    }
}
