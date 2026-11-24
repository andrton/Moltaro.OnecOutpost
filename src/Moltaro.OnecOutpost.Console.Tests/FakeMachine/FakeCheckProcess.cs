using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Commands;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// One COM worker that check or metadata started on a test's machine, of a base, under a worker account or under the
// console's own token, which FakeWorkerStarts made. The user of its connection line chooses the FakeSession that the
// test scripted for the base and that user. Its open, each run and the kill that ends it are lines of the operation
// list, never with a password: "open {base} {user}", "run {base} {user} {script}" and "kill {base} {user}", the user
// "-" before the open, the script by its kind: user for the script that gives UserName(), subscriptions for the one
// that reads Metadata.EventSubscriptions, rights for the one that calls AccessRight, other for any other; and for
// MetadataScripts.Walk, the walk of metadata, the Read that its parameters ask for, as FakeConfiguration.Describe names
// it, such as "run accounting outpost-read page Catalogs 0 2 storage". As the boundary says, it reports failures as
// results, by no exception but OperationCanceledException: an open gives what its session gives, or waits until Kill
// and then gives false; a run gives the executor's outcome with its size, or waits until Kill and then gives null; an
// open or a run of a worker that ended gives false or null at once. Its standard error, Diagnostics, ends when the
// worker ends: after an open that gave false, at Kill, or at its dispose, which closes its input. Its end as a session
// ends it, EndAsync (the S10 brief, "After the lab run of deeb370", 2), is a line "end {base} {user} within
// {deadline}": a worker that ended, or that ends at the end of its input, has exited then; one whose session lingers
// waits the deadline on the clock given, is terminated, a line "terminate {base} {user}", and has exited. Kill makes it
// exit at once; a dispose alone does not. Kill completes a pending open or run, may run beside either from any thread,
// and is idempotent. Its waits are completed asynchronously, so that no code of the caller runs inside its lock.
internal sealed class FakeCheckProcess : IWorkerProcess
{
    public const string UserScript = "user";

    public const string SubscriptionsScript = "subscriptions";

    public const string RightsScript = "rights";

    public const string WalkScript = "walk";

    private readonly FakeWorkerStarts _starts;

    // Guards every field below it. Nothing is completed under it.
    private readonly Lock _lock = new();

    private readonly List<WorkerCommandLine> _commands = [];

    private readonly TaskCompletionSource<string> _diagnostics = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private WorkerConnectionLine? _connection;

    private FakeSession? _session;

    private bool _ended;

    private bool _killed;

    private bool _disposed;

    private bool _exited;

    private bool _terminated;

    private TimeSpan? _endDeadline;

    private TaskCompletionSource<bool>? _waitingOpen;

    private TaskCompletionSource<(long Size, byte[] Outcome)?>? _waitingRun;

    public FakeCheckProcess(FakeWorkerStarts starts, string baseKey, string program, string? account)
    {
        _starts = starts;
        Base = baseKey;
        Program = program;
        Account = account;
    }

    public string Base { get; }

    public string Program { get; }

    // The worker account it runs under; null for the console's own token.
    public string? Account { get; }

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

    // The process has exited: after EndAsync or Kill.
    public bool Exited
    {
        get
        {
            lock (_lock)
            {
                return _exited;
            }
        }
    }

    // EndAsync terminated it at its deadline.
    public bool Terminated
    {
        get
        {
            lock (_lock)
            {
                return _terminated;
            }
        }
    }

    // The deadline that EndAsync was given, null before it.
    public TimeSpan? EndDeadline
    {
        get
        {
            lock (_lock)
            {
                return _endDeadline;
            }
        }
    }

    // Its standard error, read to its end.
    public Task<string> Diagnostics => _diagnostics.Task;

    // The kind of a script of check, or of the walk of metadata, as the lines of the operation list name it.
    public static string KindOf(string script)
    {
        if (script == MetadataScripts.Walk)
        {
            return WalkScript;
        }

        if (script.Contains("UserName()", StringComparison.Ordinal))
        {
            return UserScript;
        }

        if (script.Contains("EventSubscriptions", StringComparison.Ordinal))
        {
            return SubscriptionsScript;
        }

        if (script.Contains("AccessRight(", StringComparison.Ordinal))
        {
            return RightsScript;
        }

        return "other";
    }

    public Task<bool> OpenAsync(WorkerConnectionLine connection, CancellationToken cancellationToken)
    {
        FakeSession session = _starts.SessionOf(Base, connection.User);
        bool refuses = session.RefusesOpen();
        TaskCompletionSource<bool> waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool ended;

        lock (_lock)
        {
            _connection = connection;
            _session = session;
            ended = _ended;

            if (session.Hangs && !ended)
            {
                _waitingOpen = waiting;
            }
        }

        _starts.Note($"open {Base} {connection.User}");

        if (ended)
        {
            return Task.FromResult(false);
        }

        if (session.Hangs)
        {
            cancellationToken.Register(() => waiting.TrySetCanceled(cancellationToken));

            return waiting.Task;
        }

        if (refuses)
        {
            End(session.Diagnostics);

            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public Task<(long Size, byte[] Outcome)?> RunAsync(WorkerCommandLine command, CancellationToken cancellationToken)
    {
        string kind = KindOf(command.Script);
        string read = kind == WalkScript ? FakeConfiguration.Describe(command.ParametersJson) : kind;
        TaskCompletionSource<(long Size, byte[] Outcome)?> waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeSession? session;
        string user;
        bool ended;

        lock (_lock)
        {
            _commands.Add(command);
            session = _session;
            user = UserOf(_connection);
            ended = _ended || session is null;
        }

        byte[]? outcome = ended ? null : session?.OutcomeOf(kind, command.ParametersJson);

        lock (_lock)
        {
            if (!ended && outcome is null)
            {
                _waitingRun = waiting;
            }
        }

        _starts.Note($"run {Base} {user} {read}");

        if (ended)
        {
            return Task.FromResult<(long Size, byte[] Outcome)?>(null);
        }

        if (outcome is null)
        {
            cancellationToken.Register(() => waiting.TrySetCanceled(cancellationToken));

            return waiting.Task;
        }

        return Task.FromResult<(long Size, byte[] Outcome)?>((outcome.Length, outcome));
    }

    // Closes the input and waits for the exit within the deadline on the clock, then terminates the worker and waits.
    public async Task EndAsync(TimeSpan deadline, TimeProvider clock)
    {
        FakeSession? session;
        string user;
        bool ended;

        lock (_lock)
        {
            _endDeadline = deadline;
            session = _session;
            user = UserOf(_connection);
            ended = _ended || _killed;
        }

        _starts.Note($"end {Base} {user} within {deadline:c}");

        if (!ended && session is { Lingers: true })
        {
            await Task.Delay(deadline, clock);
            _starts.Note($"terminate {Base} {user}");

            lock (_lock)
            {
                _terminated = true;
            }
        }

        lock (_lock)
        {
            _exited = true;
        }

        End(session?.Diagnostics ?? "");
    }

    public void Kill()
    {
        TaskCompletionSource<bool>? open;
        TaskCompletionSource<(long Size, byte[] Outcome)?>? run;
        FakeSession? session;
        string user;

        lock (_lock)
        {
            if (_killed)
            {
                return;
            }

            _killed = true;
            _exited = true;
            open = _waitingOpen;
            run = _waitingRun;
            session = _session;
            user = UserOf(_connection);
        }

        // The line comes before anything that a test may wait for, so that a test that saw the kill finds its line.
        _starts.Note($"kill {Base} {user}");

        End(session?.Diagnostics ?? "");
        open?.TrySetResult(false);
        run?.TrySetResult(null);
    }

    public void Dispose()
    {
        FakeSession? session;

        lock (_lock)
        {
            _disposed = true;
            session = _session;
        }

        End(session?.Diagnostics ?? "");
    }

    // The user of the connection line, as the lines name the worker; "-" before its open.
    private static string UserOf(WorkerConnectionLine? connection)
    {
        return connection?.User ?? "-";
    }

    // The worker has ended, and with it its standard error.
    private void End(string diagnostics)
    {
        lock (_lock)
        {
            _ended = true;
        }

        _diagnostics.TrySetResult(diagnostics);
    }
}
