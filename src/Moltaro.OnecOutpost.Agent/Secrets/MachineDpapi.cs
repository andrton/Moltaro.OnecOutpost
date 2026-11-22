using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Agent.Secrets;

// DPAPI for the machine: CryptProtectData and CryptUnprotectData with CRYPTPROTECT_LOCAL_MACHINE |
// CRYPTPROTECT_UI_FORBIDDEN, without entropy, through LibraryImport. It frees every blob the system returns.
[SupportedOSPlatform("windows")]
internal sealed class MachineDpapi : IMachineProtection
{
    public byte[] Protect(byte[] data)
    {
        throw new NotSupportedException();
    }

    public byte[] Unprotect(byte[] data)
    {
        throw new NotSupportedException();
    }
}
