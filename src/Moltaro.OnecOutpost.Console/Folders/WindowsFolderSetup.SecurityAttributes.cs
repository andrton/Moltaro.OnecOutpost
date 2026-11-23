using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Folders;

internal sealed partial class WindowsFolderSetup
{
    // SECURITY_ATTRIBUTES, with which CreateDirectoryW makes a folder with its descriptor in one call; the handle of a
    // folder is never inherited.
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public uint Length;

        public nint SecurityDescriptor;

        public int InheritHandle;
    }
}
