using Moltaro.OnecOutpost.Agent.Checks;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Loop;

// The agent's loop (§6.2, §6.3 and §9 of docs/protocol.md): numbered polls, the checks and records of their commands,
// the runs, and the outcomes sent until confirmed. One task makes every change of the audit log and the outbox, while
// requests and runs may wait meanwhile (principle 10).
//
// How: everything the loop waits for, the poll or the wait before it, the one outcome request or the wait before the
// next, and each run, is a task that ends with what the loop does then. The requests and the runs end on any thread;
// the loop's task alone acts, one ending at a time, so that nothing it changes is shared and no lock is needed. Not
// thread-safe, and run once.
internal sealed class AgentLoop
{
    // The reason that connection lost records for an answer that CheckAnswer refuses: a 200 that is not verified (§9).
    private const string Unverified = "status-200";

    private readonly AgentRun _run;

    private readonly AgentConnection _connection;

    private readonly CommandChecks _checks;

    private readonly WorkerPool _workers;

    private readonly TimeProvider _clock;

    private readonly string _version;

    // A tenth of retryDelayMax: the first delay after a lost answer, and the wait after an older poll's answer (§6.2).
    private readonly TimeSpan _firstDelay;

    // The runs that have not ended.
    private readonly HashSet<Task<Action>> _runs = [];

    // The commands whose outcome files wait for their turn to be sent, the start's first.
    private readonly Queue<Guid> _outcomes = new();

    // The poll timeout that the next poll carries, and the lease grace that its wait adds: those last learnt, from
    // agent.json or from the last answer that was not lost.
    private TimeSpan _pollTimeout;

    private TimeSpan _leaseGrace;

    // The state of the polls that the last connection entry recorded; null while it is unknown, as at the start.
    private bool? _connected;

    // The bases of the last answer of the run that bases recorded; null before the first.
    private IReadOnlyList<string>? _held;

    private IReadOnlyList<string>? _claimed;

    // The delays that the next lost answer to a poll, and to an outcome, makes the loop wait: each starts at a tenth of
    // retryDelayMax and doubles up to it.
    private TimeSpan _pollDelay;

    private TimeSpan _outcomeDelay;

    // The open poll, or the wait before the next one; null only while the loop acts on its end.
    private Task<Action>? _poll;

    // The open outcome request, or the wait before the next one; null while no outcome is on its way.
    private Task<Action>? _outcome;

    // The token of every request, run and wait of the loop, which ends them all when the loop ends.
    private CancellationToken _stop;

    // The version is the agent's, as every poll carries it.
    public AgentLoop(
        AgentRun run,
        AgentConnection connection,
        CommandChecks checks,
        WorkerPool workers,
        TimeProvider clock,
        string version)
    {
        _run = run;
        _connection = connection;
        _checks = checks;
        _workers = workers;
        _clock = clock;
        _version = version;
        _pollTimeout = run.Pairing.PollTimeout;
        _leaseGrace = run.Pairing.LeaseGrace;
        _firstDelay = run.Policy.FirstRetryDelay;
        _pollDelay = _firstDelay;
        _outcomeDelay = _firstDelay;
    }

    // Ends by OperationCanceledException or AgentStopException. Its one task awaits whichever of the poll, the one
    // outcome request, the runs and the delays ends first.
    // - A poll has Next, the poll timeout last learnt, the policy's bases with Free and runLimit, and Unconfirmed as
    //   received. Where the sum of free would pass the room left, the modes keep theirs in the policy's order, read
    //   before write, and the rest declare 0. It opens once the last answer's commands are recorded (rule 10).
    // - A lost answer, one that CheckAnswer refuses included (status-200), records connection lost when the state is
    //   not, and nothing else: no Answered, no command, no bases, no retention. Then a growing delay: a tenth of
    //   retryDelayMax, doubled up to it, reset by an answer that is not lost.
    // - Any other answer: connection connected when needed; Answered(newest); its pollTimeoutMs and leaseGraceMs kept;
    //   each command in order, Refuse, or Received then RunAsync, or nothing for Known; bases when it differs from the
    //   run's last; RemoveExpired with its time. After an older poll's answer, a tenth of retryDelayMax. A run that
    //   ends gets End, then Release.
    // - Outcomes go one at a time, the files in turn, the start's first. Confirmed calls Confirm. TooLarge calls
    //   ReplaceForSize when the outbox file is of kind succeeded or failed with a code other than result-too-large, and
    //   the replacement goes next; for any other it is lost. After a lost answer the next file waits a growing delay of
    //   its own, so an outcome that stays lost holds the others up one delay per turn.
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _stop = stop.Token;

        try
        {
            foreach (Guid command in _run.Outbox.Commands)
            {
                _outcomes.Enqueue(command);
            }

            OpenPoll();

            while (true)
            {
                SendNextOutcome();

                Task<Action> ended = await Task.WhenAny(Pending());

                // A stop ends every request, run and wait, and the loop acts on none of them then.
                cancellationToken.ThrowIfCancellationRequested();
                Forget(ended);

                Action then = await ended;
                then();
            }
        }
        finally
        {
            // A stop of the agent's own ends what still waits too, and the loop leaves nothing running: a run that it
            // ends so kills its worker and records nothing (rule 3 at the next start).
            stop.Cancel();
            await ((Task)Task.WhenAll(Pending())).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private static bool Same(IReadOnlyList<string>? recorded, IReadOnlyList<string> given)
    {
        return recorded is not null && recorded.SequenceEqual(given, StringComparer.Ordinal);
    }

    private List<Task<Action>> Pending()
    {
        List<Task<Action>> pending = [.. _runs];

        if (_poll is not null)
        {
            pending.Add(_poll);
        }

        if (_outcome is not null)
        {
            pending.Add(_outcome);
        }

        return pending;
    }

    private void Forget(Task<Action> ended)
    {
        if (ended == _poll)
        {
            _poll = null;
        }
        else if (ended == _outcome)
        {
            _outcome = null;
        }
        else
        {
            _runs.Remove(ended);
        }
    }

    private async Task<Action> WaitAsync(TimeSpan delay, Action then)
    {
        await Task.Delay(delay, _clock, _stop);

        return then;
    }

    // Opens the next poll: its number, which records a reservation first when it needs one (§6.2), the agent's time and
    // version, the poll timeout last learnt, the bases, and every command not confirmed as received (rule 11).
    private void OpenPoll()
    {
        long number = _run.Numbers.Next();
        List<Guid> received = [.. _run.Log.Unconfirmed.Select(command => command.Id)];

        OutpostPollRequest poll = new(
            number,
            _clock.GetUtcNow(),
            _version,
            _pollTimeout,
            Declare(received.Count),
            received);

        _poll = PollAsync(poll, _leaseGrace);
    }

    // The bases of the policy with the free workers and the run limit of each mode (rules 8 and 9). The sum of free never
    // passes the room left in received (§6.2): the modes keep theirs in the policy's order, read before write, while
    // room is left, and the rest declare 0.
    private List<OutpostPollBase> Declare(int received)
    {
        int room = OutpostProtocol.MaxReceived - received;
        List<OutpostPollBase> bases = [];

        foreach (AgentBasePolicy served in _run.Policy.Bases)
        {
            OutpostPollMode read = Declare(served.Key, OutpostModeEnum.Read, served.Read, ref room);
            OutpostPollMode? write = served.Write is AgentModePolicy writes
                ? Declare(served.Key, OutpostModeEnum.Write, writes, ref room)
                : null;

            bases.Add(new OutpostPollBase(served.Key, read, write));
        }

        return bases;
    }

    private OutpostPollMode Declare(string baseKey, OutpostModeEnum mode, AgentModePolicy policy, ref int room)
    {
        int free = Math.Min(_workers.Free(baseKey, mode), room);
        room -= free;

        return new OutpostPollMode(free, policy.RunLimit);
    }

    private async Task<Action> PollAsync(OutpostPollRequest poll, TimeSpan leaseGrace)
    {
        PollResult result = await _connection.PollAsync(poll, leaseGrace, _run.Policy.RequestTimeout, _stop);

        return () => ActOnPoll(poll, result);
    }

    private void ActOnPoll(OutpostPollRequest poll, PollResult result)
    {
        if (result.Answer is not OutpostPollAnswer answer)
        {
            LosePoll(result.Reason!);

            return;
        }

        // An answer whose commands that run exceed free for a base and mode, or that holds two commands of one identity,
        // is lost as well, and none of its commands is recorded or run (§6.2).
        IReadOnlyList<CommandCheck>? checks = _checks.CheckAnswer(poll, answer, _run.Log);

        if (checks is null)
        {
            LosePoll(Unverified);

            return;
        }

        TakeAnswer(answer, checks);
    }

    // A lost answer is acted on in nothing but the state of the polls (§9); a new reason while the state stays lost is
    // no change.
    private void LosePoll(string reason)
    {
        if (_connected != false)
        {
            _run.Log.Connection(false, reason);
            _connected = false;
        }

        _poll = WaitAsync(_pollDelay, OpenPoll);
        _pollDelay = _run.Policy.NextRetryDelay(_pollDelay);
    }

    private void TakeAnswer(OutpostPollAnswer answer, IReadOnlyList<CommandCheck> checks)
    {
        if (_connected != true)
        {
            _run.Log.Connection(true, null);
            _connected = true;
        }

        // An ordinary answer ends the counting; an older poll's answer puts the next numbers above its newest, or, while
        // the agent counts, finds a cleared log and stops it (§6.2).
        _run.Numbers.Answered(answer.Newest);
        _pollTimeout = answer.PollTimeout;
        _leaseGrace = answer.LeaseGrace;

        foreach (CommandCheck check in checks)
        {
            Take(check);
        }

        if (!Same(_held, answer.Held) || !Same(_claimed, answer.Claimed))
        {
            _run.Log.Bases(answer.Held, answer.Claimed);
            _held = answer.Held;
            _claimed = answer.Claimed;
        }

        _run.Log.RemoveExpired(_run.Pairing.Agent, answer.Time, _run.Policy.AuditRetentionDays);
        _pollDelay = _firstDelay;

        // Every command of the answer is recorded by now, so the next poll may open (rule 10). After an older poll's
        // answer the loop waits, so that a duplicated or retried request cannot make it poll in a busy loop.
        if (answer.Newest is null)
        {
            OpenPoll();
        }
        else
        {
            _poll = WaitAsync(_firstDelay, OpenPoll);
        }
    }

    private void Take(CommandCheck check)
    {
        switch (check.Verdict)
        {
            case CommandVerdictEnum.Refused:
                _run.Records.Refuse(check.Id, check.BaseKey, check.Mode, check.Code!);

                // signature and form name no command, and nothing is reported for them (rule 1).
                if (check.Id is Guid refused)
                {
                    _outcomes.Enqueue(refused);
                }

                break;

            case CommandVerdictEnum.Runs:
                // Rule 2: the received is on the disk before the run starts, and a command that cannot be recorded does
                // not run, since the failed write stops the agent first.
                _run.Log.Received(check.Command!);
                _runs.Add(RunCommandAsync(check.Command!));

                break;

            case CommandVerdictEnum.Known:
                // Rule 3: the command runs once, and what is recorded of it stays as it is.
                break;
        }
    }

    // WorkerPool.RunAsync is called before the first await, on the loop's task, so that the command holds its worker
    // before the next poll declares free.
    private async Task<Action> RunCommandAsync(OutpostCommandMessage command)
    {
        WorkerRun run = await _workers.RunAsync(command, _stop);

        return () => RecordEnd(command.Id, run);
    }

    // The worker is free again once the outcome is recorded, and the outcome goes in its turn.
    private void RecordEnd(Guid command, WorkerRun run)
    {
        _run.Records.End(command, run.Outcome, run.DurationMs);
        _workers.Release(command);
        _outcomes.Enqueue(command);
    }

    private void SendNextOutcome()
    {
        if (_outcome is null && _outcomes.TryDequeue(out Guid command))
        {
            SendOutcome(command);
        }
    }

    private void SendOutcome(Guid command)
    {
        byte[] outcome = _run.Outbox.Read(command);

        _outcome = SendOutcomeAsync(command, outcome);
    }

    private async Task<Action> SendOutcomeAsync(Guid command, byte[] outcome)
    {
        OutcomeResultEnum result = await _connection.SendOutcomeAsync(command, outcome, _run.Policy.RequestTimeout, _stop);

        return () => ActOnOutcome(command, outcome, result);
    }

    // The answers to outcomes leave the state of the polls as it is (§9).
    private void ActOnOutcome(Guid command, byte[] outcome, OutcomeResultEnum result)
    {
        if (result == OutcomeResultEnum.Confirmed)
        {
            _run.Records.Confirm(command);
            _outcomeDelay = _firstDelay;

            return;
        }

        if (result == OutcomeResultEnum.TooLarge && IsReplaceable(command, outcome))
        {
            // Rule 11: the replacement is recorded, and it goes next.
            _run.Records.ReplaceForSize(command);
            _outcomeDelay = _firstDelay;
            SendOutcome(command);

            return;
        }

        // Lost: the same bytes go again in their turn, after a growing delay of the outcomes' own.
        _outcomes.Enqueue(command);
        _outcome = WaitAsync(_outcomeDelay, SendNextOutcome);
        _outcomeDelay = _run.Policy.NextRetryDelay(_outcomeDelay);
    }

    // An outcome of kind succeeded or failed is replaced once for its size; one of kind refused or unknown never is, and
    // one with the code result-too-large counts as replaced already (the S6 brief, "TooLarge"). The bytes are those of
    // the outbox file, which the start and the records keep valid for the command's mode.
    private bool IsReplaceable(Guid command, byte[] outcome)
    {
        OutpostOutcomeMessage message = OutpostCodec.ReadOutcome(_run.Log.Find(command)?.Mode, outcome);

        return message.Kind is OutpostAgentOutcomeKindEnum.Succeeded or OutpostAgentOutcomeKindEnum.Failed
            && message.Code != OutpostOutcomeCodes.ResultTooLarge;
    }
}
