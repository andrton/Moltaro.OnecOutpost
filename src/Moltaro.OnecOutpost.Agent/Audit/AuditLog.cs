using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Audit;

// The audit log in audit\ (§13 of docs/protocol.md), held through its lock file without sharing while the agent runs.
// It reads the files named YYYY-MM-DD.jsonl at its opening, cutting a torn last line, and adds one entry at a time,
// each with one Append to the file of the UTC day of its time and on the disk before the writer returns. Not
// thread-safe: one task makes every change of the agent's files. A failed write cuts the file back, sets Fault and
// throws AgentStopException; with Fault set, every writer but Started writes nothing and throws AgentStopException,
// and after a failed write Started too.
internal sealed class AuditLog : IDisposable
{
    // Takes the lock of the folder, whose IOException passes and records nothing, then reads every audit file in the
    // order of their names, cuts a last line without its LF or that is not JSON, and parses the lines with the depth of
    // a message. A line it cannot read, or a file it cannot open, sets Fault and ends the reading there.
    public static AuditLog Open(IAgentFolder audit, TimeProvider clock)
    {
        throw new NotSupportedException();
    }

    // Why the log cannot be trusted, naming the file and the line, or the failed write; null when it can.
    public string? Fault => throw new NotSupportedException();

    // The names of the audit files it read.
    public IReadOnlyList<string> Files => throw new NotSupportedException();

    // Every command that an entry names and no confirmed does: the received list of a poll (§6.2).
    public IReadOnlyCollection<AuditCommand> Unconfirmed => throw new NotSupportedException();

    // The facts of a command that a received, a refused with a command, an ended or a confirmed names; null for any
    // other.
    public AuditCommand? Find(Guid command)
    {
        throw new NotSupportedException();
    }

    // The greatest below of the reservations of that identity, in whichever files they are; null when there is none.
    public long? HighestReservation(Guid agent)
    {
        throw new NotSupportedException();
    }

    // Records started: the agent's version, the keys of the bases that write without safe mode, and the error that
    // stops the start, null when there is none.
    public void Started(string version, IReadOnlyList<string> withoutSafeMode, string? error)
    {
        throw new NotSupportedException();
    }

    // Records policy, valid exactly when the error is null.
    public void Policy(string? error)
    {
        throw new NotSupportedException();
    }

    // Records connection, connected or lost, with the reason of §9.
    public void Connection(bool connected, string? reason)
    {
        throw new NotSupportedException();
    }

    // Records bases: the bases the agent holds and those it claims.
    public void Bases(IReadOnlyList<string> held, IReadOnlyList<string> claimed)
    {
        throw new NotSupportedException();
    }

    // Records received: the command, its script and its parameters, written again with OutpostJsonWriter.Value.
    public void Received(OutpostCommandMessage command)
    {
        throw new NotSupportedException();
    }

    // Records refused with its code: the command, its base and its mode where they are known, none of them for signature
    // and form.
    public void Refused(Guid? command, string? baseKey, OutpostModeEnum? mode, string code)
    {
        throw new NotSupportedException();
    }

    // Records ended: the outcome succeeded, failed or unknown, its code and its duration, null for one that a start
    // writes.
    public void Ended(Guid command, OutpostAgentOutcomeKindEnum kind, string? code, long? durationMs)
    {
        throw new NotSupportedException();
    }

    // Records confirmed: the server keeps the outcome of the command.
    public void Confirmed(Guid command)
    {
        throw new NotSupportedException();
    }

    // Records reservation: the bound below which the identity sends its poll numbers (§6.2).
    public void Reservation(Guid agent, long below)
    {
        throw new NotSupportedException();
    }

    // The whole rule of retention, called after each verified poll answer with its time: nothing when the clock is more
    // than a day away from that time, either way; otherwise it deletes, in the order of their names, every file whose
    // last entry is more than retentionDays plus a day before the clock, and every file with no entry, except a file
    // with an entry of an unconfirmed command and the file of the highest reservation of the identity. A file it cannot
    // delete stays until a later call, and nothing is recorded.
    public void RemoveExpired(Guid agent, DateTimeOffset answerTime, int retentionDays)
    {
        throw new NotSupportedException();
    }

    // Releases the lock of the folder.
    public void Dispose()
    {
        throw new NotSupportedException();
    }
}
