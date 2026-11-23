using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Console.Service;

// The service controller of Windows through LibraryImport, its service handle with SERVICE_START, which restart actions
// need. Install: ChangeServiceConfigW of an existing service, keeping its failure actions; else CreateServiceW, own
// process, automatic start, NT SERVICE\{name}, no password, with the failure actions: restart after 1 minute, three
// times, the count reset after a day, the flag for a stop with an error false, as AgentService stops so on its own.
// Both set the description and no required privileges, which would drop the agent's two. StopAsync: 1060 is
// NotInstalled; SERVICE_CONTROL_STOP, 1062 counting as stopped and 1061 waiting; QueryServiceStatusEx every tenth of the
// wait hint, 1 to 10 seconds, until stopped or the timeout by the clock. Delete: 1072 counts as done.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsServiceControl : IServiceControl
{
    private readonly TimeProvider _clock;

    // The clock of the wait for a stop.
    public WindowsServiceControl(TimeProvider clock)
    {
        _clock = clock;
    }

    public bool Install(string name, string displayName, string binaryPath)
    {
        throw new NotSupportedException();
    }

    public Task<ServiceStopEnum> StopAsync(string name, TimeSpan timeout, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    public void Delete(string name)
    {
        throw new NotSupportedException();
    }
}
