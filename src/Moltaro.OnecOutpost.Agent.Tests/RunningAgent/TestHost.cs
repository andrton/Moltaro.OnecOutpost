using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Tests.FakeFiles;
using Moltaro.OnecOutpost.Agent.Tests.FakeHttp;
using Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.RunningAgent;

// The agent of a PairedFolder running in the process, as AgentHost.RunAsync runs it, over the fakes: its data folder
// in memory, the fake access lists and protection, the fake workers, the fake clock, and the FakeServer as the handler
// of its connection, given by AgentParts.Handlers as the S6 brief says the tests give it. The fake server and the fake
// workers note their operations in the list of the data folder, so that a test asserts their order among the writes.
// The tests of the loop drive it request by request; a restart is a second TestHost on the same folder, with a fake
// server of its own and, after the clock was set back, a clock of its own.
internal sealed class TestHost : IAsyncDisposable
{
    public const string Executor = @"C:\Program Files\Moltaro Outpost\OutpostExecutor.epf";

    public static readonly TimeSpan Millisecond = TimeSpan.FromMilliseconds(1);

    private readonly ECDsa _serverKey;

    private readonly ECDsa _agentKey;

    private readonly HostRun _run;

    private TestHost(PairedFolder folder, FakeClock clock, IWorkerLauncher workers)
    {
        Folder = folder;
        Clock = clock;

        _serverKey = ECDsa.Create();
        _serverKey.ImportPkcs8PrivateKey(folder.ServerPrivateKey, out _);
        _agentKey = ECDsa.Create();
        _agentKey.ImportPkcs8PrivateKey(folder.PrivateKey, out _);

        Server = new FakeServer(_serverKey, clock, folder.Data) { AgentKey = _agentKey };

        AgentParts parts = new(
            folder.Data.Folders,
            folder.Access,
            folder.Protection,
            workers,
            PairedFolder.Programs,
            Executor,
            (_, _) => Server,
            clock,
            PairedFolder.Version);

        _run = HostRun.Start(parts);
    }

    public PairedFolder Folder { get; }

    public MemoryDataFolder Data => Folder.Data;

    public FakeClock Clock { get; }

    public FakeServer Server { get; }

    public Task<int> Exit => _run.Exit;

    // Starts the agent on the folder, with fake workers that open at once and give a Succeeded outcome unless a test
    // gives a launcher of its own.
    public static TestHost Start(PairedFolder folder, FakeClock clock, IWorkerLauncher? workers = null)
    {
        return new TestHost(folder, clock, workers ?? new FakeWorkerLauncher(folder.Data));
    }

    public Task<ServerExchange> NextPollAsync()
    {
        return Server.NextPollAsync();
    }

    public Task<ServerExchange> NextOutcomeAsync()
    {
        return Server.NextOutcomeAsync();
    }

    // Completes once the agent has armed a timer due at or before dueBy.
    public Task WaitForTimerAsync(DateTimeOffset dueBy)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return Clock.WaitForTimerAsync(dueBy, cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
    }

    // Waits until the agent has armed a timer due by the delay from now, such as a delay before its next request, and
    // moves the clock by the delay.
    public async Task PassAsync(TimeSpan delay)
    {
        await WaitForTimerAsync(Clock.GetUtcNow() + delay);
        Clock.Advance(delay);
    }

    // The same, and states that the timer is due exactly then: none is due a millisecond earlier.
    public async Task PassExactlyAsync(TimeSpan delay)
    {
        DateTimeOffset now = Clock.GetUtcNow();

        await WaitForTimerAsync(now + delay);
        Assert.False(Clock.HasTimerDueBy(now + delay - Millisecond), $"The agent waits less than {delay}.");

        Clock.Advance(delay);
    }

    // The exit code, once the agent ends by itself.
    public Task<int> ExitAsync()
    {
        return _run.ExitAsync();
    }

    // Stops the agent as the service controller does, and gives its exit code.
    public Task<int> StopAsync()
    {
        return _run.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _run.DisposeAsync();
        _serverKey.Dispose();
        _agentKey.Dispose();
    }
}
