using Moltaro.OnecOutpost.Agent.Checks;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Agent.Loop;

// The agent's loop (§6.2, §6.3 and §9 of docs/protocol.md): numbered polls, the checks and records of their commands,
// the runs, and the outcomes sent until confirmed. One task makes every change of the audit log and the outbox, while
// requests and runs may wait meanwhile (principle 10).
internal sealed class AgentLoop
{
    // The version is the agent's, as every poll carries it.
    public AgentLoop(
        AgentRun run,
        AgentConnection connection,
        CommandChecks checks,
        WorkerPool workers,
        TimeProvider clock,
        string version)
    {
        throw new NotSupportedException();
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
    public Task RunAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
