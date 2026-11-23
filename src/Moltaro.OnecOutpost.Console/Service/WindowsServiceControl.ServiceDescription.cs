using System.Runtime.InteropServices;

namespace Moltaro.OnecOutpost.Console.Service;

internal sealed partial class WindowsServiceControl
{
    // SERVICE_DESCRIPTIONW of winsvc.h: the text that the Services console shows for the service.
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceDescription
    {
        public nint Text;
    }
}
