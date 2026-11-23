using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class ComWorkerLauncher
{
    // STARTUPINFOEXW of winbase.h, with its STARTUPINFOW laid out in place, in their order: the sequential layout aligns
    // each field as the C compiler does, for 64 and for 32 bits. The strings are pointers that the caller keeps pinned;
    // a field that a start does not set stays zero.
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        // STARTUPINFOW.
        public int Size;

        public nint Reserved;

        public nint Desktop;

        public nint Title;

        public int X;

        public int Y;

        public int XSize;

        public int YSize;

        public int XCountChars;

        public int YCountChars;

        public int FillAttribute;

        public int Flags;

        public short ShowWindow;

        public short Reserved2Size;

        public nint Reserved2;

        public nint StandardInput;

        public nint StandardOutput;

        public nint StandardError;

        // The list of attributes of the extended form.
        public nint AttributeList;
    }
}
