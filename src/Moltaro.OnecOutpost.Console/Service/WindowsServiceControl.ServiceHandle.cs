using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Console.Service;

internal sealed partial class WindowsServiceControl
{
    // A handle of the service controller or of a service, closed once with CloseServiceHandle. OpenSCManagerW,
    // OpenServiceW and CreateServiceW give null when they fail, so zero is the invalid handle. The marshalling of
    // LibraryImport makes it with the parameterless constructor.
    private sealed partial class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle()
            : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return CloseServiceHandle(handle);
        }

        [LibraryImport("advapi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseServiceHandle(nint handle);
    }
}
