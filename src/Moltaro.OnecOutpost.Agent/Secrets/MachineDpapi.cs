using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Moltaro.OnecOutpost.Agent.Secrets;

// DPAPI for the machine: CryptProtectData and CryptUnprotectData with CRYPTPROTECT_LOCAL_MACHINE |
// CRYPTPROTECT_UI_FORBIDDEN, without entropy, through LibraryImport. It frees every blob the system returns.
[SupportedOSPlatform("windows")]
internal sealed partial class MachineDpapi : IMachineProtection
{
    // CRYPTPROTECT_UI_FORBIDDEN | CRYPTPROTECT_LOCAL_MACHINE: no prompt, since a service has nobody to ask, and a blob
    // that any account of this machine can unprotect, so that the agent's account reads what the console wrote.
    private const int Flags = 0x1 | 0x4;

    public byte[] Protect(byte[] data)
    {
        return Transform(data, protect: true);
    }

    public byte[] Unprotect(byte[] data)
    {
        return Transform(data, protect: false);
    }

    private static unsafe byte[] Transform(byte[] data, bool protect)
    {
        fixed (byte* input = data)
        {
            DataBlob dataIn = new() { Size = data.Length, Data = (nint)input };
            DataBlob dataOut;

            bool done = protect
                ? CryptProtectData(in dataIn, 0, 0, 0, 0, Flags, out dataOut)
                : CryptUnprotectData(in dataIn, 0, 0, 0, 0, Flags, out dataOut);

            if (!done)
            {
                throw new CryptographicException(Marshal.GetLastPInvokeError());
            }

            // The system allocated the blob it returned, and LocalFree gives it back once its bytes are copied.
            try
            {
                return new ReadOnlySpan<byte>((void*)dataOut.Data, dataOut.Size).ToArray();
            }
            finally
            {
                LocalFree(dataOut.Data);
            }
        }
    }

    // The description, the entropy, the reserved pointer and the prompt are all null.
    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(
        in DataBlob dataIn,
        nint description,
        nint entropy,
        nint reserved,
        nint prompt,
        int flags,
        out DataBlob dataOut);

    // The description is not asked for, and the entropy, the reserved pointer and the prompt are null.
    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(
        in DataBlob dataIn,
        nint description,
        nint entropy,
        nint reserved,
        nint prompt,
        int flags,
        out DataBlob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
