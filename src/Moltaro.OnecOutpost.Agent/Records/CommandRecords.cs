using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Records;

// The one place for the order of the records of a command that touch both the audit log and the outbox (rules 2, 3
// and 11); the loop writes received with AuditLog.Received. An outcome that the agent writes itself is
// OutpostCodec.Write of one with no result, no error text and no messages. Not thread-safe: one task makes every change
// of the agent's files.
internal sealed class CommandRecords
{
    // Checks what a restart left and writes nothing. It throws AgentStopException for an outbox file that ReadOutcome
    // refuses for the mode of its command, or either mode when no entry names one; for a file of a command that no
    // entry names; for a file of kind refused that does not match a refused of its command, or of another kind for a
    // command that only a refused names; and for a command, not confirmed, that has an ended and no file.
    public static CommandRecords Open(AuditLog log, Outbox outbox)
    {
        throw new NotSupportedException();
    }

    // Called after started and policy, writes what the restart rules need: it removes the file of a confirmed command;
    // gives a file whose kind and code differ from the last ended or refused of its command, or that has neither, an
    // ended that matches it; writes again the file of a refused with an identity, not confirmed; and gives a received
    // with neither file nor ended the file of Unknown agent-stopped for a Write or Failed lost for a Read, then its
    // ended. Every ended it writes has the durationMs of the last ended or null. A stop in the middle leaves what the
    // next start finishes.
    public void Recover()
    {
        throw new NotSupportedException();
    }

    // Writes refused, then, for a command with an identity, the file of its Refused outcome with the code. signature and
    // form have no identity and write only the entry.
    public void Refuse(Guid? command, string? baseKey, OutpostModeEnum? mode, string code)
    {
        throw new NotSupportedException();
    }

    // Writes the outcome file, then ended with the kind and the code that OutpostCodec.ReadOutcome reads from the bytes
    // for the command's mode, and the duration. The bytes are an outcome that the loop checked by §8.
    public void End(Guid command, ReadOnlySpan<byte> outcome, long durationMs)
    {
        throw new NotSupportedException();
    }

    // For a command whose outcome file is of kind succeeded or failed, writes the file of Unknown result-too-large for a
    // Write or Failed result-too-large for a Read, then a second ended with it and the durationMs that Find gives.
    public void ReplaceForSize(Guid command)
    {
        throw new NotSupportedException();
    }

    // Writes confirmed, then removes the outcome file.
    public void Confirm(Guid command)
    {
        throw new NotSupportedException();
    }
}
