using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Rights;

internal sealed partial class WindowsAccountRights
{
    // LSA_OBJECT_ATTRIBUTES, which LsaOpenPolicy takes with nothing but its length.
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public uint Length;

        public nint RootDirectory;

        public nint ObjectName;

        public uint Attributes;

        public nint SecurityDescriptor;

        public nint SecurityQualityOfService;
    }
}
