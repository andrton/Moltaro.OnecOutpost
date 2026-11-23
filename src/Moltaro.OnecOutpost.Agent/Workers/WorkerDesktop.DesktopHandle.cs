using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class WorkerDesktop
{
    // A handle of a desktop, closed once with CloseDesktop. CreateDesktopW gives null when it fails, so zero is the
    // invalid handle. The marshalling of LibraryImport makes it with the parameterless constructor. A desktop lives
    // while a handle or a thread uses it, so running workers keep theirs after the handle is closed.
    private sealed partial class DesktopHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public DesktopHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return CloseDesktop(handle);
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseDesktop(nint desktop);
    }
}
