using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class WorkerDesktop
{
    // SECURITY_ATTRIBUTES of minwinbase.h: the security descriptor of a new desktop, whose handle no child inherits.
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;

        public nint SecurityDescriptor;

        public int InheritHandle;
    }
}
