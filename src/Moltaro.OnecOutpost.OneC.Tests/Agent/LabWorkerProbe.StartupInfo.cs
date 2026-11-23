using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.OneC.Tests.Agent;

internal static partial class LabWorkerProbe
{
    // STARTUPINFOW of processthreadsapi.h, in its order: the sequential layout aligns each field as the C compiler does,
    // for 64 and for 32 bits. The desktop is a pointer that the caller keeps pinned; a field that the probe does not set
    // stays zero.
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
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
    }
}
