using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;

// One fake COM worker process, started by FakeWorkerLauncher for a base and a mode. Its name is the base, the mode and
// its number among the starts of that base and mode, such as "accounting write 1"; every OpenAsync and RunAsync, and
// the Kill that ends it, is a line of the operation list: "open {name}", "run {name}" and "kill {name}". It keeps the
// lines it was given, so that a test reads them. As the boundary says, it reports failures as results, by no
// exception but OperationCanceledException:
// - an open gives true or false at once, or, for FakeOpenEnum.Never, waits until Kill ends the process and then gives
//   false;
// - a run gives the outcome of its FakeRun at once, with its size, or null at once; a run that overruns gives nothing
//   until Kill ends the process, and then null;
// - an open or a run that waits is cancelled by its token;
// - an open of a process that has ended gives false at once, and a run null at once.
// Kill completes a pending open with false and a pending run with null, and may run beside either and beside another
// Kill, from any thread. It is idempotent: only the first ends the process and is a line. Its waits are completed
// asynchronously, so that no code of the caller runs inside the fake's lock.
internal sealed class FakeWorkerProcess : IWorkerProcess
{
    private readonly FakeWorkerLauncher _launcher;

    // Guards every field below it. Nothing is completed under it.
    private readonly Lock _lock = new();

    private readonly List<WorkerCommandLine> _commands = [];

    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private WorkerConnectionLine? _connection;

    private bool _killed;

    private bool _disposed;

    private TaskCompletionSource<bool>? _waitingOpen;

    private TaskCompletionSource<(long Size, byte[] Outcome)?>? _waitingRun;

    public FakeWorkerProcess(FakeWorkerLauncher launcher, string baseKey, OutpostModeEnum mode, int number, string? account)
    {
        _launcher = launcher;
        Base = baseKey;
        Mode = mode;
        Number = number;
        Account = account;
    }

    public string Base { get; }

    public OutpostModeEnum Mode { get; }

    public int Number { get; }

    // The worker account it runs under, that of its base logged on before it started; null for the agent's own.
    public string? Account { get; }

    public string Name => $"{Base} {(Mode == OutpostModeEnum.Read ? "read" : "write")} {Number}";

    // The connection line of its open, null before it.
    public WorkerConnectionLine? Connection
    {
        get
        {
            lock (_lock)
            {
                return _connection;
            }
        }
    }

    // The command lines of its runs, in their order.
    public IReadOnlyList<WorkerCommandLine> Commands
    {
        get
        {
            lock (_lock)
            {
                return [.. _commands];
            }
        }
    }

    // Completes when Kill ends the process: an event a test waits for.
    public Task Killed => _ended.Task;

    public bool IsKilled => _ended.Task.IsCompleted;

    public bool IsDisposed
    {
        get
        {
            lock (_lock)
            {
                return _disposed;
            }
        }
    }

    public Task<bool> OpenAsync(WorkerConnectionLine connection, CancellationToken cancellationToken)
    {
        FakeOpenEnum open = _launcher.Opens(Base, Mode, Number);
        TaskCompletionSource<bool> waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool ended;

        lock (_lock)
        {
            _connection = connection;
            ended = _killed;

            if (open == FakeOpenEnum.Never && !ended)
            {
                _waitingOpen = waiting;
            }
        }

        _launcher.Note($"open {Name}");

        if (ended)
        {
            return Task.FromResult(false);
        }

        if (open == FakeOpenEnum.Never)
        {
            cancellationToken.Register(() => waiting.TrySetCanceled(cancellationToken));

            return waiting.Task;
        }

        return Task.FromResult(open == FakeOpenEnum.Ready);
    }

    public Task<(long Size, byte[] Outcome)?> RunAsync(WorkerCommandLine command, CancellationToken cancellationToken)
    {
        FakeRun run = _launcher.Runs(command);
        TaskCompletionSource<(long Size, byte[] Outcome)?> waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool ended;

        lock (_lock)
        {
            _commands.Add(command);
            ended = _killed;

            if (run.WaitsForKill && !ended)
            {
                _waitingRun = waiting;
            }
        }

        _launcher.Note($"run {Name}");

        if (ended)
        {
            return Task.FromResult<(long Size, byte[] Outcome)?>(null);
        }

        if (run.WaitsForKill)
        {
            cancellationToken.Register(() => waiting.TrySetCanceled(cancellationToken));

            return waiting.Task;
        }

        if (run.Outcome is byte[] outcome)
        {
            return Task.FromResult<(long Size, byte[] Outcome)?>((outcome.Length + run.SizeOff, outcome));
        }

        return Task.FromResult<(long Size, byte[] Outcome)?>(null);
    }

    public void Kill()
    {
        TaskCompletionSource<bool>? open;
        TaskCompletionSource<(long Size, byte[] Outcome)?>? run;

        lock (_lock)
        {
            if (_killed)
            {
                return;
            }

            _killed = true;
            open = _waitingOpen;
            run = _waitingRun;
        }

        // The line comes before anything that a test may wait for, so that a test that saw the kill finds its line.
        _launcher.Note($"kill {Name}");

        open?.TrySetResult(false);
        run?.TrySetResult(null);
        _ended.TrySetResult();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
        }
    }
}
