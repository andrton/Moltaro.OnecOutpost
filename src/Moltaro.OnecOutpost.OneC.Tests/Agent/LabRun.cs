using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport.Workers;

namespace Moltaro.OnecOutpost.OneC.Tests.Agent;

// One run of a LabAgent, AgentHost.RunAsync as the service runs it (the S9b brief, "The lab tests"): on the agent's
// data folder, with the machine's access lists and DPAPI, the real ComWorkerLauncher with the worker beside the tests as
// both programs, x64 on the lab, in the agent's job, the executor of the repository, the agent's fake clock and a
// LabServer of its own as the handler of its connection, which the connection owns. The launcher is wrapped in
// TestSupport's ObservedLauncher, which only reports when an open ended and when a run began, so that a test moves the
// clock only after it observed the script running. A test stops the run as the service controller stops the service,
// by cancelling its token.
[SupportedOSPlatform("windows")]
internal sealed class LabRun : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();

    private readonly Task<int> _exit;

    private LabRun(LabAgent agent)
    {
        Server = new LabServer(agent.ServerKey, agent.AgentKey, agent.Agent, agent.Clock);
        Workers = new ObservedLauncher(new ComWorkerLauncher(
            Repository.WorkerPath,
            Repository.WorkerPath,
            ComWorkerLauncher.ComConnector,
            agent.Job));

        AgentParts parts = new(
            agent.Folders,
            new WindowsAccessLists(agent.DataPath),
            new MachineDpapi(),
            Workers,
            AppContext.BaseDirectory,
            Repository.ExecutorPath,
            (_, _) => Server,
            agent.Clock,
            LabAgent.Version);

        _exit = Task.Run(() => AgentHost.RunAsync(parts, _stop.Token));
    }

    public LabServer Server { get; }

    public ObservedLauncher Workers { get; }

    public static LabRun Start(LabAgent agent)
    {
        return new LabRun(agent);
    }

    // Completes once the open of the number-th worker of the mode ended, with what it gave.
    public Task<bool> OpenedAsync(OutpostModeEnum mode, int number = 1)
    {
        return Within(Workers.OpenedAsync(LabAgent.BaseKey, mode, number));
    }

    // Completes once the number-th run of the mode began, with its command line.
    public Task<WorkerCommandLine> RanAsync(OutpostModeEnum mode, int number = 1)
    {
        return Within(Workers.RanAsync(LabAgent.BaseKey, mode, number));
    }

    // Hands the test base a command, and gives its identity.
    public Guid Send(OutpostModeEnum mode, string script, string parameters = "{}")
    {
        return Server.Send(LabAgent.BaseKey, mode, script, parameters);
    }

    // The outcome of the command, once the agent sent it.
    public Task<OutpostOutcomeMessage> OutcomeAsync(Guid command)
    {
        return Within(Server.OutcomeAsync(command));
    }

    // Completes once a poll listed the command as received.
    public Task ReceivedAsync(Guid command)
    {
        return Within(Server.ReceivedAsync(command));
    }

    // Stops the run, and gives its exit code.
    public Task<int> StopAsync()
    {
        _stop.Cancel();

        return Within(_exit);
    }

    // A run that a test left running is stopped; how it ended was the test's to state.
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await ((Task)_exit).WaitAsync(LabAgent.Deadline).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stop.Dispose();
    }

    private static Task<T> Within<T>(Task<T> wait)
    {
        return wait.WaitAsync(LabAgent.Deadline, TestContext.Current.CancellationToken);
    }

    private static Task Within(Task wait)
    {
        return wait.WaitAsync(LabAgent.Deadline, TestContext.Current.CancellationToken);
    }
}
