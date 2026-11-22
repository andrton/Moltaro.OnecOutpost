using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Agent.Secrets;

internal sealed partial class MachineDpapi
{
    // DATA_BLOB of wincrypt.h: a count of bytes and a pointer to them, the form in which DPAPI takes and gives data.
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;

        public nint Data;
    }
}
