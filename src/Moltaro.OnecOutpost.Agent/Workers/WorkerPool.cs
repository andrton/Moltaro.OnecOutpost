using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The COM workers of every base and mode of the policy, and the run limits of their commands (concept rules 8 and 9),
// with what turns an executor's object into the outcome sent. Not thread-safe, and no lock: the loop's task calls it, a
// run's task changes only its own worker, and Release reads what that left after the loop awaited the run. An open's
// task sets only its worker's opened flag, with Volatile.Write, and the loop reads it with Volatile.Read: the flag only
// goes from false to true, so a late read under-declares free and is safe.
internal sealed partial class WorkerPool : IAsyncDisposable
{
    private readonly AgentPolicy _policy;

    private readonly AgentSecrets _secrets;

    private readonly IWorkerLauncher _launcher;

    private readonly string _executor;

    private readonly TimeProvider _clock;

    // The workers of each base and mode, in the order of their starts. Only the loop's task adds to the lists.
    private readonly Dictionary<(string Base, OutpostModeEnum Mode), List<Worker>> _workers = [];

    // The worker that each command holds, from RunAsync to Release.
    private readonly Dictionary<Guid, Worker> _held = [];

    // The executor is the path of OutpostExecutor.epf that every connection line names.
    public WorkerPool(AgentPolicy policy, AgentSecrets secrets, IWorkerLauncher launcher, string executor, TimeProvider clock)
    {
        _policy = policy;
        _secrets = secrets;
        _launcher = launcher;
        _executor = executor;
        _clock = clock;
    }

    // Starts the opens of the workers of every mode and returns at once. Each races Task.Delay(runLimit, clock): the
    // mode's run limit is its deadline (principle 10, concept §5), and a late worker is killed and, like one that gives
    // false, never free. So the loop starts at once, and a slow base holds up no other.
    public void Open(CancellationToken cancellationToken)
    {
        foreach (AgentBasePolicy served in _policy.Bases)
        {
            OpenMode(served, OutpostModeEnum.Read, served.Read, cancellationToken);

            if (served.Write is AgentModePolicy write)
            {
                OpenMode(served, OutpostModeEnum.Write, write, cancellationToken);
            }
        }
    }

    // Counts a mode's workers whose open ended with true and that hold no command. A command holds one from RunAsync,
    // called right after its received, to Release, called after End: it counts from its acceptance until its outcome is
    // recorded.
    public int Free(string baseKey, OutpostModeEnum mode)
    {
        if (!_workers.TryGetValue((baseKey, mode), out List<Worker>? workers))
        {
            return 0;
        }

        return workers.Count(worker => worker.IsFree);
    }

    // Takes a free worker of the command's base and mode before it returns; with none, it gives Failed base-unreachable
    // at once, in either mode, as the script never started (§8). A run races RunAsync with Task.Delay(runLimit, clock).
    // When the delay wins: Kill, a wait for the run's end, and run-limit, whatever the run then gives. Otherwise null
    // gives connection-lost; a size other than the length of the bytes, bytes that ReadOutcome refuses for the mode, and
    // a kind and code that IsExecutorOutcome refuses give result-type; anything else goes byte for byte. Each code is
    // Failed for a Read and Unknown for a Write, written as CommandRecords writes its own. DurationMs is the clock's
    // time from the call to the outcome. A worker is dropped after null, after a kill, and after it gave
    // base-unreachable or connection-lost, after which S9's worker ends; until S9b starts it again with a growing delay
    // (concept §5), its mode has one worker fewer. Cancellation kills the worker, waits for its run and throws,
    // recording nothing (rule 3 at the next start).
    public Task<WorkerRun> RunAsync(OutpostCommandMessage command, CancellationToken cancellationToken)
    {
        long started = _clock.GetTimestamp();
        Worker? worker = _workers.GetValueOrDefault((command.Base, command.Mode))?.FirstOrDefault(free => free.IsFree);

        if (worker is null)
        {
            byte[] unreachable = Own(OutpostAgentOutcomeKindEnum.Failed, OutpostOutcomeCodes.BaseUnreachable);

            return Task.FromResult(new WorkerRun(unreachable, ElapsedMs(started)));
        }

        // The worker is held before the run starts, so that Free never counts it for a poll meanwhile.
        _held.Add(command.Id, worker);
        worker.IsHeld = true;

        Task<WorkerRun> run = RunOnAsync(worker, command, started, cancellationToken);
        worker.Running = run;

        return run;
    }

    // Frees the worker that the command holds, after End recorded its outcome.
    public void Release(Guid command)
    {
        if (_held.Remove(command, out Worker? worker))
        {
            worker.IsHeld = false;
        }
    }

    // Kills and disposes every worker.
    public async ValueTask DisposeAsync()
    {
        List<Worker> workers = [.. _workers.Values.SelectMany(mode => mode)];

        foreach (Worker worker in workers)
        {
            worker.Process.Kill();
        }

        // Kill completes a pending open with false and a pending run with null (IWorkerProcess), so every task of a
        // worker ends; what a run gave belongs to its caller, and nothing uses a process once it is disposed.
        IEnumerable<Task> tasks = workers.SelectMany(worker => new[] { worker.Opening, worker.Running });
        await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        foreach (Worker worker in workers)
        {
            worker.Process.Dispose();
        }
    }

    // An outcome that the agent writes itself, as CommandRecords writes its own: no result, no error text and no
    // messages.
    private static byte[] Own(OutpostAgentOutcomeKindEnum kind, string code)
    {
        return OutpostCodec.Write(new OutpostOutcomeMessage(kind, code, null, null, [], false));
    }

    // The outcome that the agent writes itself for a command that may have run: Failed for a Read, and Unknown for a
    // Write, whose script may have committed (rule 5).
    private static byte[] OwnAfterStart(OutpostModeEnum mode, string code)
    {
        OutpostAgentOutcomeKindEnum kind = mode == OutpostModeEnum.Write
            ? OutpostAgentOutcomeKindEnum.Unknown
            : OutpostAgentOutcomeKindEnum.Failed;

        return Own(kind, code);
    }

    // The object of §8 that the bytes hold for the mode, or null when ReadOutcome refuses them.
    private static OutpostOutcomeMessage? ReadOrNull(OutpostModeEnum mode, byte[] outcome)
    {
        try
        {
            return OutpostCodec.ReadOutcome(mode, outcome);
        }
        catch (OutpostProtocolException)
        {
            return null;
        }
    }

    // The outcome sent for what a run gave within its limit (§8): the executor's bytes as they came when the agent may
    // accept them, and otherwise an outcome of the agent's own. The worker is dropped when it ended, or broke its line,
    // without an outcome, and when it gave base-unreachable or connection-lost, after which S9's worker ends.
    private static byte[] Accept(Worker worker, OutpostModeEnum mode, (long Size, byte[] Outcome)? given)
    {
        if (given is not (long size, byte[] outcome))
        {
            worker.Drop();

            return OwnAfterStart(mode, OutpostOutcomeCodes.ConnectionLost);
        }

        OutpostOutcomeMessage? message = ReadOrNull(mode, outcome);

        if (message?.Code is OutpostOutcomeCodes.BaseUnreachable or OutpostOutcomeCodes.ConnectionLost)
        {
            worker.Drop();
        }

        // S9's requirement: the size the executor measured is the length of the bytes the agent received.
        bool accepted = size == outcome.Length
            && message is not null
            && OutpostOutcomeCodes.IsExecutorOutcome(mode, message.Kind, message.Code);

        return accepted ? outcome : OwnAfterStart(mode, OutpostOutcomeCodes.ResultType);
    }

    // The base as S9's worker completes it into a 1C connection string: Srvr and Ref for a cluster, File for a folder,
    // each " in a value doubled.
    private static string LocationOf(AgentBasePolicy served)
    {
        if (served.File is string file)
        {
            return $"File=\"{Quoted(file)}\";";
        }

        return $"Srvr=\"{Quoted(served.Server!)}\";Ref=\"{Quoted(served.Database!)}\";";
    }

    private static string Quoted(string value)
    {
        return value.Replace("\"", "\"\"", StringComparison.Ordinal);
    }

    // Starts the workers of one mode and their opens, all with the connection line of the mode.
    private void OpenMode(AgentBasePolicy served, OutpostModeEnum mode, AgentModePolicy policy, CancellationToken cancellationToken)
    {
        WorkerConnectionLine connection = new()
        {
            Location = LocationOf(served),
            User = policy.Account,
            Password = _secrets.Passwords[(served.Key, mode)],
            SafeMode = policy.SafeMode,
            Executor = _executor,
        };

        List<Worker> workers = [];
        _workers[(served.Key, mode)] = workers;

        for (int number = 0; number < policy.Workers; number++)
        {
            Worker worker = new(_launcher.Start(served.Key, mode), policy.RunLimit);

            workers.Add(worker);
            worker.Opening = OpenWorkerAsync(worker, connection, cancellationToken);
        }
    }

    // The open of one worker, raced with the run limit of its mode, its deadline. An open that answers at once has ended
    // when this returns. Its task never fails: a worker whose open gives false, comes late or is cancelled is never free.
    private async Task OpenWorkerAsync(Worker worker, WorkerConnectionLine connection, CancellationToken cancellationToken)
    {
        using CancellationTokenSource race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // The process is given the caller's token, so that ending the race below leaves it as it is.
        Task<bool> open = worker.Process.OpenAsync(connection, cancellationToken);
        Task deadline = Task.Delay(worker.RunLimit, _clock, race.Token);

        Task first = await Task.WhenAny(open, deadline);

        if (first == open && open.IsCompletedSuccessfully)
        {
            if (open.Result)
            {
                worker.Opened();
            }
        }
        else if (first == deadline && deadline.IsCompletedSuccessfully)
        {
            // Late: the worker is ended and never runs a command, and its open ends with the process.
            worker.Process.Kill();
        }

        // Ends the timer of the deadline.
        race.Cancel();
        await ((Task)open).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    // The run of one command on the worker it holds, raced with the run limit of its mode.
    private async Task<WorkerRun> RunOnAsync(
        Worker worker,
        OutpostCommandMessage command,
        long started,
        CancellationToken cancellationToken)
    {
        WorkerCommandLine line = new(
            command.Script,
            command.Parameters.GetRawText(),
            command.CallerKey,
            command.OutcomeLimit);

        using CancellationTokenSource race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<(long Size, byte[] Outcome)?> run;

        // The process is given the caller's token, as its open is. A token already cancelled may end RunAsync before it
        // returns a task; the worker is then killed all the same.
        try
        {
            run = worker.Process.RunAsync(line, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            worker.Drop();
            throw;
        }

        Task limit = Task.Delay(worker.RunLimit, _clock, race.Token);

        Task first = await Task.WhenAny(run, limit);

        if (first == run && !run.IsCanceled)
        {
            // Ends the timer of the run limit. A fault, which the boundary never gives, passes.
            race.Cancel();
            (long Size, byte[] Outcome)? given = await run;

            return new WorkerRun(Accept(worker, command.Mode, given), ElapsedMs(started));
        }

        // The run limit passed, or the run was cancelled: the worker ends, whatever it was doing, and its run is waited
        // for, so that nothing of it is left running. What it gives then is not looked at.
        worker.Drop();
        await ((Task)run).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (first == limit && limit.IsCompletedSuccessfully)
        {
            return new WorkerRun(OwnAfterStart(command.Mode, OutpostOutcomeCodes.RunLimit), ElapsedMs(started));
        }

        throw new OperationCanceledException(cancellationToken);
    }

    private long ElapsedMs(long started)
    {
        return (long)_clock.GetElapsedTime(started).TotalMilliseconds;
    }
}
