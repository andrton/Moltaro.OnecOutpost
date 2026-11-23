using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The COM workers of every base and mode of the policy, the run limits of their commands (concept rules 8 and 9) with
// what turns an executor's object into the outcome sent, and the slots that start the workers again with a growing
// delay (concept §5). No lock; who owns what:
// - the loop's task calls Open, Free, RunAsync and Release, and owns the lists of slots, the held workers and each
//   worker's IsHeld and Running;
// - the task of each slot starts, opens and disposes the workers of its slot, one at a time, and publishes the one
//   whose open gave true with Volatile.Write; Free and RunAsync read it with Volatile.Read, so a worker read late is one
//   not yet opened and free is only under-declared. Start may run on several slots' tasks at once;
// - the task of a run changes only its own worker: it drops it, and then completes the worker's end after its last use
//   of the process. Release reads what it left after the loop awaited the run;
// - a slot touches its published worker again only after the worker's end, or once the pool's token is cancelled. Only
//   DisposeAsync cancels it, and the host disposes the pool after the loop ended, so no run begins after that, and a
//   slot reads the Running task that the loop set only after the cancellation.
internal sealed partial class WorkerPool : IAsyncDisposable
{
    private readonly AgentPolicy _policy;

    private readonly AgentSecrets _secrets;

    private readonly IWorkerLauncher _launcher;

    private readonly string _executor;

    private readonly TimeProvider _clock;

    // A tenth of retryDelayMax: the delay of a slot after a worker whose open gave true, and its first after one whose
    // open did not.
    private readonly TimeSpan _firstDelay;

    // The slots of each base and mode, in the order of their first starts. Only the loop's task adds to the lists.
    private readonly Dictionary<(string Base, OutpostModeEnum Mode), List<Slot>> _slots = [];

    // The task of every slot, which DisposeAsync waits for.
    private readonly List<Task> _running = [];

    // The worker that each command holds, from RunAsync to Release.
    private readonly Dictionary<Guid, Worker> _held = [];

    // The token of the slots, which they pass to their delays and opens. Only DisposeAsync cancels it, first: a stop of
    // the host ends the loop, whose runs then kill their workers, and only after that the slots, so that no slot
    // disposes a process that a run still uses.
    private CancellationTokenSource? _stop;

    // The executor is the path of OutpostExecutor.epf that every connection line names.
    public WorkerPool(AgentPolicy policy, AgentSecrets secrets, IWorkerLauncher launcher, string executor, TimeProvider clock)
    {
        _policy = policy;
        _secrets = secrets;
        _launcher = launcher;
        _executor = executor;
        _clock = clock;
        _firstDelay = policy.RetryDelayMax / 10;
    }

    // Gives each mode of every base as many slots as it has workers, each with a task of its own, and returns at once;
    // every slot's first start happens before it returns, in the policy's order. Each open races
    // Task.Delay(runLimit, clock): the mode's run limit is its deadline (principle 10, concept §5), and a late worker is
    // killed and, like one that gives false, never free. So the loop starts at once, and a slow base holds up no other.
    public void Open()
    {
        _stop = new CancellationTokenSource();

        foreach (AgentBasePolicy served in _policy.Bases)
        {
            OpenMode(served, OutpostModeEnum.Read, served.Read, _stop.Token);

            if (served.Write is AgentModePolicy write)
            {
                OpenMode(served, OutpostModeEnum.Write, write, _stop.Token);
            }
        }
    }

    // Counts a mode's workers whose open ended with true and that hold no command. A command holds one from RunAsync,
    // called right after its received, to Release, called after End: it counts from its acceptance until its outcome is
    // recorded.
    public int Free(string baseKey, OutpostModeEnum mode)
    {
        if (!_slots.TryGetValue((baseKey, mode), out List<Slot>? slots))
        {
            return 0;
        }

        return slots.Count(slot => slot.Worker is { IsFree: true });
    }

    // Takes a free worker of the command's base and mode before it returns; with none, it gives Failed base-unreachable
    // at once, in either mode, as the script never started (§8). A run races RunAsync with Task.Delay(runLimit, clock).
    // When the delay wins: Kill, a wait for the run's end, and run-limit, whatever the run then gives. Otherwise null
    // gives connection-lost; a size other than the length of the bytes, bytes that ReadOutcome refuses for the mode, and
    // a kind and code that IsComWorkerOutcome refuses give result-type; anything else goes byte for byte. Each code is
    // Failed for a Read and Unknown for a Write, written as CommandRecords writes its own. DurationMs is the clock's
    // time from the call to the outcome. A worker is dropped after null, after a kill, and after it gave
    // base-unreachable or connection-lost, after which S9's worker ends; its mode has one worker fewer until its slot
    // starts another. Cancellation kills the worker, waits for its run and throws, recording nothing (rule 3 at the next
    // start).
    public Task<WorkerRun> RunAsync(OutpostCommandMessage command, CancellationToken cancellationToken)
    {
        long started = _clock.GetTimestamp();
        List<Slot>? slots = _slots.GetValueOrDefault((command.Base, command.Mode));
        Worker? worker = slots?.Select(slot => slot.Worker).FirstOrDefault(published => published is { IsFree: true });

        if (worker is null)
        {
            byte[] unreachable = OwnOutcome.Write(
                OutpostAgentOutcomeKindEnum.Failed,
                OutpostOutcomeCodes.BaseUnreachable);

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

    // Cancels the pool's token first, then waits for every slot, and disposes no worker itself: on the cancellation
    // each slot kills its worker, waits for its open and for the run that holds it, if any, disposes it and ends, and a
    // slot that starts a worker after the cancellation kills and disposes it at once.
    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? stop = _stop;

        // A pool never opened has no slot, and a second dispose finds nothing left.
        if (stop is null)
        {
            return;
        }

        _stop = null;
        stop.Cancel();
        await Task.WhenAll(_running);
        stop.Dispose();
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

    // The outcome sent for what a run gave within its limit (§8): the bytes of the executor, or of S9's worker itself,
    // as they came when the agent may accept them, and otherwise an outcome of the agent's own. The worker is dropped
    // when it ended, or broke its line, without an outcome, and when it gave base-unreachable or connection-lost, after
    // which S9's worker ends.
    private static byte[] Accept(Worker worker, OutpostModeEnum mode, (long Size, byte[] Outcome)? given)
    {
        if (given is not (long size, byte[] outcome))
        {
            worker.Drop();

            return OwnOutcome.AfterStart(mode, OutpostOutcomeCodes.ConnectionLost);
        }

        OutpostOutcomeMessage? message = ReadOrNull(mode, outcome);

        if (message?.Code is OutpostOutcomeCodes.BaseUnreachable or OutpostOutcomeCodes.ConnectionLost)
        {
            worker.Drop();
        }

        // S9's requirement: the size the executor measured is the length of the bytes the agent received.
        bool accepted = size == outcome.Length
            && message is not null
            && OutpostOutcomeCodes.IsComWorkerOutcome(mode, message.Kind, message.Code);

        return accepted ? outcome : OwnOutcome.AfterStart(mode, OutpostOutcomeCodes.ResultType);
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

    // Gives one mode its slots, which open their workers with the connection line of the mode.
    private void OpenMode(AgentBasePolicy served, OutpostModeEnum mode, AgentModePolicy policy, CancellationToken stop)
    {
        WorkerConnectionLine connection = new()
        {
            Location = LocationOf(served),
            User = policy.Account,
            Password = _secrets.Passwords[(served.Key, mode)],
            SafeMode = policy.SafeMode,
            Executor = _executor,
        };

        List<Slot> slots = [];
        _slots[(served.Key, mode)] = slots;

        for (int number = 0; number < policy.Workers; number++)
        {
            Slot slot = new();

            slots.Add(slot);
            _running.Add(RunSlotAsync(slot, served.Key, mode, policy.RunLimit, connection, stop));
        }
    }

    // One slot, which holds one worker at a time until the pool's token is cancelled. It runs synchronously up to the
    // open of its first worker, so that the start is made before Open returns. On a worker's end it disposes the
    // process, waits a delay on the pool's clock and starts a new worker. The old process was killed and may still be
    // closing its 1C session; the new one may be free before the old command's Release.
    private async Task RunSlotAsync(
        Slot slot,
        string baseKey,
        OutpostModeEnum mode,
        TimeSpan runLimit,
        WorkerConnectionLine connection,
        CancellationToken stop)
    {
        TimeSpan delay = TimeSpan.Zero;

        while (true)
        {
            Worker worker = new(_launcher.Start(baseKey, mode), runLimit);

            if (stop.IsCancellationRequested)
            {
                // Started while the pool is disposed: never opened.
                worker.Process.Kill();
                worker.Process.Dispose();

                return;
            }

            bool opened = await OpenWorkerAsync(slot, worker, connection, stop);

            await worker.Ended.WaitAsync(stop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            slot.Publish(null);

            if (stop.IsCancellationRequested)
            {
                // The kill ends the run that holds the worker, if any, and nothing of that run uses the process once it
                // ended.
                worker.Process.Kill();
                await worker.Running.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                worker.Process.Dispose();

                return;
            }

            worker.Process.Dispose();
            delay = opened ? _firstDelay : Doubled(delay);

            await Task.Delay(delay, _clock, stop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (stop.IsCancellationRequested)
            {
                return;
            }
        }
    }

    // Twice the delay before a worker whose open did not give true, from a tenth of retryDelayMax up to retryDelayMax:
    // 3, 6, 12, 24, 30, 30 … seconds by default, the first worker of a slot having waited none.
    private TimeSpan Doubled(TimeSpan before)
    {
        long ticks = Math.Clamp(before.Ticks * 2, _firstDelay.Ticks, _policy.RetryDelayMax.Ticks);

        return TimeSpan.FromTicks(ticks);
    }

    // The open of one worker, raced with the run limit of its mode, its deadline; true when it gave true in time. The
    // slot publishes a worker that opened before the timer of the deadline ends, so that once that timer is gone, Free
    // counts the worker. A worker whose open gives false, comes late or is cancelled is dropped, and its end is
    // completed once its open ended with the process. An open that answers at once has ended when this returns. Its
    // task never fails.
    private async Task<bool> OpenWorkerAsync(
        Slot slot,
        Worker worker,
        WorkerConnectionLine connection,
        CancellationToken stop)
    {
        using CancellationTokenSource race = CancellationTokenSource.CreateLinkedTokenSource(stop);

        // The process is given the pool's token, so that ending the race below leaves it as it is.
        Task<bool> open = worker.Process.OpenAsync(connection, stop);
        Task deadline = Task.Delay(worker.RunLimit, _clock, race.Token);

        Task first = await Task.WhenAny(open, deadline);
        bool opened = first == open && open.IsCompletedSuccessfully && open.Result;

        // Ends the timer of the deadline, after the publish of a worker that opened.
        if (opened)
        {
            slot.Publish(worker);
            race.Cancel();

            return true;
        }

        race.Cancel();

        // The worker ends and never runs a command, and its open ends with the process.
        worker.Drop();
        await ((Task)open).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        worker.End();

        return false;
    }

    // The run of one command on the worker it holds, raced with the run limit of its mode. The end of a worker that it
    // dropped is completed last, after its last use of the process, so that its slot disposes the process only then.
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

        try
        {
            Task<(long Size, byte[] Outcome)?> run;

            // The process is given the caller's token, as its open is given the pool's: each call passes on the token
            // it was given, never the race's. A token already cancelled may end RunAsync before it returns a task; the
            // worker is then killed all the same.
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

            // The run limit passed, or the run was cancelled: the worker ends, whatever it was doing, and its run is
            // waited for, so that nothing of it is left running. What it gives then is not looked at.
            worker.Drop();
            await ((Task)run).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (first == limit && limit.IsCompletedSuccessfully)
            {
                return new WorkerRun(
                    OwnOutcome.AfterStart(command.Mode, OutpostOutcomeCodes.RunLimit),
                    ElapsedMs(started));
            }

            throw new OperationCanceledException(cancellationToken);
        }
        finally
        {
            if (worker.IsDropped)
            {
                worker.End();
            }
        }
    }

    private long ElapsedMs(long started)
    {
        return (long)_clock.GetElapsedTime(started).TotalMilliseconds;
    }
}
