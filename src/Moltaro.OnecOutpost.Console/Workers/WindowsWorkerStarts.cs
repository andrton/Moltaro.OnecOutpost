using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Moltaro.OnecOutpost.Console.Workers;

// The COM workers of check on Windows: the logon by WorkerLogon, the desktop by WorkerDesktop.Make, the process by
// WorkerCreation, AsUser with the console's own token and WithToken with a worker account's, each in a WorkerJob of its
// own, since a process already in a job, the Secondary Logon service's or a terminal's, joins only an empty job or one
// of its hierarchy. The clock gives the probe's deadline.
[SupportedOSPlatform("windows")]
internal sealed class WindowsWorkerStarts(TimeProvider clock) : IWorkerStarts
{
    private const string NotYet = "The COM workers of check are S10b's implementation.";

    // The clock of the probe's deadline, which the implementation reads.
    private TimeProvider Clock { get; } = clock;

    public IWorkerAccount LogOn(string baseKey, string account, string password)
    {
        throw new NotSupportedException(NotYet);
    }

    public Task<uint?> ProbeAsync(IWorkerAccount account, string program, string baseKey, TimeSpan deadline, CancellationToken cancellationToken)
    {
        throw new NotSupportedException(NotYet);
    }

    public CheckWorker Start(IWorkerAccount? account, string program, string baseKey)
    {
        throw new NotSupportedException(NotYet);
    }

    // The account of a token that the caller logged on itself, such as the process's own in a test, which it then owns.
    public IWorkerAccount Account(string baseKey, SafeAccessTokenHandle token)
    {
        throw new NotSupportedException(NotYet);
    }
}
