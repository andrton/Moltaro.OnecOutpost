using System.Runtime.Versioning;
using System.Text.Json;
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
// data folder, with the machine's access lists and DPAPI, the real ComWorkerLauncher with the worker of the agent's
// program folder as both programs, x64 on the lab, in the agent's job, that folder as the program folder that the start
// judges (the S9c brief), the executor in it, the agent's fake clock and a LabServer of its own as the handler of its
// connection, which the connection owns. The launcher is wrapped in
// TestSupport's ObservedLauncher, which only reports when an open ended and when a run began, so that a test moves the
// clock only after it observed the script running. A test stops the run as the service controller stops the service,
// by cancelling its token. When a worker does not open, WhyNotOpened gives the test's message what the agent recorded
// and what its launcher gave.
[SupportedOSPlatform("windows")]
internal sealed class LabRun : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();

    private readonly Task<int> _exit;

    // The key of the agent's base.
    private readonly string _key;

    private readonly string _dataPath;

    private readonly string _workerPath;

    // The account under which the agent runs the workers of its base, or null for its own.
    private readonly LabAccount? _workerAccount;

    private readonly ComWorkerLauncher _launcher;

    private LabRun(LabAgent agent)
    {
        _key = agent.Key;
        _dataPath = agent.DataPath;
        _workerPath = agent.WorkerPath;
        _workerAccount = agent.WorkerAccount;
        Server = new LabServer(agent.ServerKey, agent.AgentKey, agent.Agent, agent.Clock);
        _launcher = new ComWorkerLauncher(agent.WorkerPath, agent.WorkerPath, ComWorkerLauncher.ComConnector, agent.Job);
        Workers = new ObservedLauncher(_launcher);

        AgentParts parts = new(
            agent.Folders,
            new WindowsAccessLists(agent.DataPath),
            new MachineDpapi(),
            Workers,
            agent.ProgramsPath,
            agent.ExecutorPath,
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

    // Completes once the open of the number-th worker of the mode ended, with what it gave, or with false once the run
    // ended before it, as after a stop at the start.
    public async Task<bool> OpenedAsync(OutpostModeEnum mode, int number = 1)
    {
        Task<bool> opened = Workers.OpenedAsync(_key, mode, number);

        await Within(Task.WhenAny(opened, _exit));

        return opened.IsCompletedSuccessfully && opened.Result;
    }

    // Why the number-th worker of the mode did not open, for the message of a test; empty when it opened. What the agent
    // recorded: whether it runs or how its run ended, and the error of the last started entry of its audit log, such as
    // the reason of a logon that failed. What its launcher gave for each start of the mode: a process, whose open did
    // not give ready, or none, as for a connector that is not registered or a program that did not start. The agent
    // keeps no reason for a worker that does not start, and the launcher keeps no standard error of a worker, by
    // design; so for a base with a worker account the message adds LabWorkerProbe's start of the program under that
    // account, with its Win32 error or its exit code. No part holds a password.
    public string WhyNotOpened(OutpostModeEnum mode, int number = 1)
    {
        Task<bool> opened = Workers.OpenedAsync(_key, mode, number);

        if (opened.IsCompletedSuccessfully && opened.Result)
        {
            return "";
        }

        IReadOnlyList<IWorkerProcess> given = Workers.Given(_key, mode);
        string starts = given.Count == 0
            ? "no worker of the mode started"
            : string.Join(
                ", ",
                given.Select((process, index) => process is ComWorkerProcess
                    ? $"start {index + 1} gave a process whose open did not give ready"
                    : $"start {index + 1} gave no process, as for a connector that is not registered or a program that did not start"));

        List<string> parts =
        [
            RunState(),
            LastStarted(),
            starts,
            "the agent keeps no reason for a worker that does not start, and no standard error of a worker",
        ];

        if (_workerAccount is not null)
        {
            parts.Add(LabWorkerProbe.Describe(_workerAccount, _workerPath, _key, LabAgent.Deadline));
        }

        return string.Join("; ", parts);
    }

    // Completes once the number-th run of the mode began, with its command line.
    public Task<WorkerCommandLine> RanAsync(OutpostModeEnum mode, int number = 1)
    {
        return Within(Workers.RanAsync(_key, mode, number));
    }

    // Hands the agent's base a command, and gives its identity.
    public Guid Send(OutpostModeEnum mode, string script, string parameters = "{}")
    {
        return Server.Send(_key, mode, script, parameters);
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
        _launcher.Dispose();
        _stop.Dispose();
    }

    // What a line of the audit log says of a start, when it is a whole started entry.
    private static string? StartedOf(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement entry = document.RootElement;

            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("entry", out JsonElement kind)
                || kind.GetString() != "started")
            {
                return null;
            }

            return entry.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.String
                ? $"started recorded the error: {error.GetString()}"
                : "started recorded no error";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task<T> Within<T>(Task<T> wait)
    {
        return wait.WaitAsync(LabAgent.Deadline, TestContext.Current.CancellationToken);
    }

    private static Task Within(Task wait)
    {
        return wait.WaitAsync(LabAgent.Deadline, TestContext.Current.CancellationToken);
    }

    // Whether the agent runs, or how its run ended.
    private string RunState()
    {
        if (!_exit.IsCompleted)
        {
            return "the agent runs";
        }

        return _exit.IsCompletedSuccessfully
            ? $"the agent exited with {_exit.Result}"
            : $"the run of the agent failed: {_exit.Exception?.GetBaseException().Message}";
    }

    // The last started entry of the audit log, read beside the agent, which appends to its files while it runs; a line
    // that is not whole yet is passed over.
    private string LastStarted()
    {
        string? started = null;

        try
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(_dataPath, "audit"), "*.jsonl").Order(StringComparer.Ordinal))
            {
                using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader reader = new(stream);

                while (reader.ReadLine() is string line)
                {
                    started = StartedOf(line) ?? started;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"the audit log cannot be read: {exception.Message}";
        }

        return started ?? "the audit log holds no started entry";
    }
}
