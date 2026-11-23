using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Rights;

internal sealed partial class WindowsAccountRights
{
    // LSA_UNICODE_STRING: the name of a right in UTF-16, its length in bytes without a terminating null.
    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;

        public ushort MaximumLength;

        public nint Buffer;
    }
}
