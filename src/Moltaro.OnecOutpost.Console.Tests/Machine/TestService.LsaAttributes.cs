using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

internal sealed partial class TestService
{
    // LSA_OBJECT_ATTRIBUTES, which LsaOpenPolicy takes with nothing but its length.
    [StructLayout(LayoutKind.Sequential)]
    private struct LsaAttributes
    {
        public uint Length;

        public nint RootDirectory;

        public nint ObjectName;

        public uint Attributes;

        public nint SecurityDescriptor;

        public nint SecurityQualityOfService;
    }
}
