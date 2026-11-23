using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class WorkerDesktop
{
    // EXPLICIT_ACCESS_W of accctrl.h, with its TRUSTEE_W laid out in place, in their order: the sequential layout aligns
    // each field as the C compiler does, for 64 and for 32 bits. The trustee is a SID that the caller keeps pinned; a
    // field that the station's entry does not set stays zero: no multiple trustee, and a trustee of an unknown type.
    [StructLayout(LayoutKind.Sequential)]
    private struct ExplicitAccess
    {
        public uint AccessPermissions;

        public int AccessMode;

        public uint Inheritance;

        // TRUSTEE_W.
        public nint MultipleTrustee;

        public int MultipleTrusteeOperation;

        public int TrusteeForm;

        public int TrusteeType;

        public nint TrusteeName;
    }
}
