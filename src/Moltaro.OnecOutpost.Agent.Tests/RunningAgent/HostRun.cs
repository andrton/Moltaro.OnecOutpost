using Moltaro.OnecOutpost.Agent.Host;

namespace Moltaro.OnecOutpost.Agent.Tests.RunningAgent;

// One run of the agent in the process, AgentHost.RunAsync on the parts a test gives, as the service runs it, and its
// exit code. A test stops it as the service controller stops the service, by cancelling its token. It runs on the
// thread pool, so that nothing the start does on its caller's thread holds up the test.
internal sealed class HostRun : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();

    private HostRun(AgentParts parts)
    {
        Exit = Task.Run(() => AgentHost.RunAsync(parts, _stop.Token));
    }

    // The exit code of the run, once it ended.
    public Task<int> Exit { get; }

    public static HostRun Start(AgentParts parts)
    {
        return new HostRun(parts);
    }

    // The exit code, once the run ends by itself; it fails the test when the run does not end within the deadline.
    public Task<int> ExitAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return Exit.WaitAsync(TestTimes.Deadline, cancellationToken);
    }

    // Stops the run, and gives its exit code.
    public Task<int> StopAsync()
    {
        _stop.Cancel();

        return ExitAsync();
    }

    // A run that a test left running is stopped; how it ended was the test's to state.
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await ((Task)Exit).WaitAsync(TestTimes.Deadline).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stop.Dispose();
    }
}
