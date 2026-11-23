using Moltaro.OnecOutpost.Console.Service;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// The service controller of a test's machine. Each call is first a line of the operation list of the machine's data
// folder: "stop {name} within {timeout}", "install {name} as {displayName} running {binaryPath}" or "delete {name}".
// Then it fails with the exception the test gave for it, as Windows fails one with a Win32Exception, or answers: a stop
// with what the test says, NotInstalled on a machine without the service; Install creates the service, or reconfigures
// it once one exists. Not thread-safe: a command makes one call at a time.
internal sealed class FakeServiceControl : IServiceControl
{
    private readonly MemoryDataFolder _notes;

    public FakeServiceControl(MemoryDataFolder notes)
    {
        _notes = notes;
    }

    // Whether the service exists, so that Install reconfigures it.
    public bool Exists { get; set; }

    public ServiceStopEnum StopResult { get; set; } = ServiceStopEnum.NotInstalled;

    public Exception? StopFailure { get; set; }

    public Exception? InstallFailure { get; set; }

    public Exception? DeleteFailure { get; set; }

    public bool Install(string name, string displayName, string binaryPath)
    {
        _notes.Note($"install {name} as {displayName} running {binaryPath}");

        if (InstallFailure is not null)
        {
            throw InstallFailure;
        }

        bool created = !Exists;
        Exists = true;

        return created;
    }

    public Task<ServiceStopEnum> StopAsync(string name, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _notes.Note($"stop {name} within {timeout}");

        if (StopFailure is not null)
        {
            return Task.FromException<ServiceStopEnum>(StopFailure);
        }

        return Task.FromResult(StopResult);
    }

    public void Delete(string name)
    {
        _notes.Note($"delete {name}");

        if (DeleteFailure is not null)
        {
            throw DeleteFailure;
        }

        Exists = false;
    }
}
