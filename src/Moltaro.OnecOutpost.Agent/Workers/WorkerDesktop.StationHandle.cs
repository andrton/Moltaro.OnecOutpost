using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class WorkerDesktop
{
    // A handle of a window station, closed once with CloseWindowStation. CreateWindowStationW gives null when it fails,
    // so zero is the invalid handle. The marshalling of LibraryImport makes it with the parameterless constructor.
    private sealed partial class StationHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public StationHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return CloseWindowStation(handle);
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseWindowStation(nint station);
    }
}
