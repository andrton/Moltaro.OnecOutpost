using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class WorkerJob
{
    // The handle of the job object, closed once with CloseHandle. CreateJobObjectW gives null when it fails, so zero is
    // the invalid handle. The marshalling of LibraryImport makes it with the parameterless constructor.
    private sealed partial class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public JobHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);
    }
}
