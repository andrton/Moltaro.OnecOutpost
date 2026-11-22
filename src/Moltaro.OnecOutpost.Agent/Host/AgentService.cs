using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Agent.Host;

// The Windows service: the dispatcher, the handler and SetServiceStatus through LibraryImport, the entry points static
// and [UnmanagedCallersOnly], catching everything, so no delegate lives. ServiceMain reports SERVICE_RUNNING, accepting
// stop and shutdown, and runs the run on its thread; stop and shutdown report STOP_PENDING and cancel from the thread
// pool; SERVICE_STOPPED comes last, on every stop, with 0 or ERROR_SERVICE_SPECIFIC_ERROR and the code, so that the
// failure actions, which restart only a process that ends without reporting, leave a deliberate stop with an error
// stopped. The wait hint of STOP_PENDING is no wait of the agent: no option.
[SupportedOSPlatform("windows")]
internal static class AgentService
{
    // Runs run as the service and gives its exit code. Error 1063, a process that the service controller did not
    // start, gives NotService before the data folder is touched.
    public static int Run(Func<CancellationToken, Task<int>> run)
    {
        throw new NotSupportedException();
    }
}
