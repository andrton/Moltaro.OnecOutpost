using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.TestSupport.Workers;

// A launcher of the tests that wraps another, such as the real ComWorkerLauncher, only to report what its workers did,
// so that a test waits for an observed event before it moves a fake clock or hands a command to a free worker, as
// AGENTS.md requires. It reports when the open of the n-th worker started for a
// base and mode, counted from 1, ended, with what it gave; and when the n-th run on the workers of a base and mode began,
// counted from 1, with its command line. A run has begun once the wrapped worker's RunAsync returned its task, so that
// its command line is written or on its way. It keeps the processes that the wrapped launcher gave, so that a test
// whose worker did not open tells a start that gave no process from a process whose open failed. Every call goes to
// the wrapped launcher and its workers as it came, and what they give is given back as it is. Start may run on several
// tasks at once; the reports are completed asynchronously, so that no test goes on inside a call of the agent.
internal sealed partial class ObservedLauncher : IWorkerLauncher
{
    private readonly IWorkerLauncher _launcher;

    // Guards every field below it. Nothing is completed under it.
    private readonly Lock _lock = new();

    private readonly Dictionary<(string Base, OutpostModeEnum Mode), List<IWorkerProcess>> _given = [];

    private readonly Dictionary<(string Base, OutpostModeEnum Mode), int> _runs = [];

    private readonly Dictionary<(string Base, OutpostModeEnum Mode, int Number), TaskCompletionSource<bool>> _opens = [];

    private readonly Dictionary<(string Base, OutpostModeEnum Mode, int Number), TaskCompletionSource<WorkerCommandLine>> _began = [];

    public ObservedLauncher(IWorkerLauncher launcher)
    {
        _launcher = launcher;
    }

    public void LogOn(string baseKey, string account, string password)
    {
        _launcher.LogOn(baseKey, account, password);
    }

    public IWorkerProcess Start(string baseKey, OutpostModeEnum mode)
    {
        IWorkerProcess given = _launcher.Start(baseKey, mode);
        int number;

        lock (_lock)
        {
            if (!_given.TryGetValue((baseKey, mode), out List<IWorkerProcess>? processes))
            {
                processes = [];
                _given[(baseKey, mode)] = processes;
            }

            processes.Add(given);
            number = processes.Count;
        }

        return new ObservedProcess(this, given, baseKey, mode, number);
    }

    // The processes that the wrapped launcher gave for the base and mode, in the order of the starts, as it gave them.
    public IReadOnlyList<IWorkerProcess> Given(string baseKey, OutpostModeEnum mode)
    {
        lock (_lock)
        {
            return _given.TryGetValue((baseKey, mode), out List<IWorkerProcess>? processes) ? [.. processes] : [];
        }
    }

    // Completes when the open of the number-th worker started for the base and mode has ended: true when it gave true,
    // false when it gave false or was cancelled.
    public Task<bool> OpenedAsync(string baseKey, OutpostModeEnum mode, int number)
    {
        return Event(_opens, (baseKey, mode, number)).Task;
    }

    // Completes when the number-th run on the workers of the base and mode began, with its command line.
    public Task<WorkerCommandLine> RanAsync(string baseKey, OutpostModeEnum mode, int number)
    {
        return Event(_began, (baseKey, mode, number)).Task;
    }

    private void ReportOpened(string baseKey, OutpostModeEnum mode, int number, bool ready)
    {
        Event(_opens, (baseKey, mode, number)).TrySetResult(ready);
    }

    private void ReportRunBegan(string baseKey, OutpostModeEnum mode, WorkerCommandLine command)
    {
        int number;

        lock (_lock)
        {
            number = _runs.GetValueOrDefault((baseKey, mode)) + 1;
            _runs[(baseKey, mode)] = number;
        }

        Event(_began, (baseKey, mode, number)).TrySetResult(command);
    }

    // The event of the key, made by whichever comes first: the report or the test that waits for it.
    private TaskCompletionSource<T> Event<T>(
        Dictionary<(string Base, OutpostModeEnum Mode, int Number), TaskCompletionSource<T>> events,
        (string Base, OutpostModeEnum Mode, int Number) key)
    {
        lock (_lock)
        {
            if (!events.TryGetValue(key, out TaskCompletionSource<T>? reported))
            {
                reported = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                events[key] = reported;
            }

            return reported;
        }
    }
}
