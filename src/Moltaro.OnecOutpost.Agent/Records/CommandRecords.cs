using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Records;

// The one place for the order of the records of a command that touch both the audit log and the outbox (rules 2, 3
// and 11); the loop writes received with AuditLog.Received. An outcome that the agent writes itself is
// OutpostCodec.Write of one with no result, no error text and no messages. Not thread-safe: one task makes every change
// of the agent's files.
internal sealed class CommandRecords
{
    private readonly AuditLog _log;

    private readonly Outbox _outbox;

    private CommandRecords(AuditLog log, Outbox outbox)
    {
        _log = log;
        _outbox = outbox;
    }

    // Checks what a restart left and writes nothing. It throws AgentStopException for an outbox file that ReadOutcome
    // refuses for the mode of its command, or either mode when no entry names one; for a file of a command that no
    // entry names; for a file of kind refused that does not match a refused of its command, or of another kind for a
    // command that only a refused names; and for a command, not confirmed, that has an ended and no file.
    public static CommandRecords Open(AuditLog log, Outbox outbox)
    {
        CommandRecords records = new(log, outbox);

        foreach (Guid command in outbox.Commands)
        {
            AuditCommand facts = log.Find(command)
                ?? throw new AgentStopException($@"outcomes\{command}.json belongs to a command that the audit log does not name");

            OutpostOutcomeMessage outcome = records.ReadFile(facts);

            // A Refused outcome is wholly given by the refused of its command, and a command refused never ran.
            bool refusedFile = outcome.Kind == OutpostAgentOutcomeKindEnum.Refused;
            bool refusedCommand = facts.Kind == OutpostAgentOutcomeKindEnum.Refused;

            if (refusedFile != refusedCommand || (refusedFile && outcome.Code != facts.Code))
            {
                throw new AgentStopException($@"outcomes\{command}.json does not match what the audit log records of its command");
            }
        }

        // An outcome recorded as ended is in the outbox until it is confirmed; without its file it cannot be sent.
        foreach (AuditCommand facts in log.Unconfirmed)
        {
            bool ended = facts.Kind is not null && facts.Kind != OutpostAgentOutcomeKindEnum.Refused;

            if (ended && !outbox.Commands.Contains(facts.Id))
            {
                throw new AgentStopException($@"the audit log records the end of command {facts.Id}, but its outcome file outcomes\{facts.Id}.json is missing");
            }
        }

        return records;
    }

    // Called after started and policy, writes what the restart rules need: it removes the file of a confirmed command;
    // gives a file whose kind and code differ from the last ended or refused of its command, or that has neither, an
    // ended that matches it; writes again the file of a refused with an identity, not confirmed; and gives a received
    // with neither file nor ended the file of Unknown agent-stopped for a Write or Failed lost for a Read, then its
    // ended. Every ended it writes has the durationMs of the last ended or null. A stop in the middle leaves what the
    // next start finishes.
    public void Recover()
    {
        foreach (Guid command in _outbox.Commands.ToList())
        {
            AuditCommand facts = _log.Find(command)!;

            if (facts.Confirmed)
            {
                _outbox.Remove(command);

                continue;
            }

            // A stop between a file and its ended, or between the replacement of a file for its size and its second
            // ended. The start did not see the run, so it makes up no duration.
            OutpostOutcomeMessage outcome = ReadFile(facts);

            if (outcome.Kind != facts.Kind || outcome.Code != facts.Code)
            {
                _log.Ended(command, outcome.Kind, outcome.Code, facts.DurationMs);
            }
        }

        foreach (AuditCommand facts in _log.Unconfirmed)
        {
            if (_outbox.Commands.Contains(facts.Id))
            {
                continue;
            }

            if (facts.Kind == OutpostAgentOutcomeKindEnum.Refused)
            {
                // A stop between a refused and its file.
                _outbox.Put(facts.Id, OwnOutcome(OutpostAgentOutcomeKindEnum.Refused, facts.Code!));
            }
            else if (facts.Received && facts.Kind is null)
            {
                // Rule 3: a received command without an outcome ran, or may have, and nobody knows how it ended.
                (OutpostAgentOutcomeKindEnum kind, string code) = facts.Mode == OutpostModeEnum.Write
                    ? (OutpostAgentOutcomeKindEnum.Unknown, OutpostOutcomeCodes.AgentStopped)
                    : (OutpostAgentOutcomeKindEnum.Failed, OutpostOutcomeCodes.Lost);

                _outbox.Put(facts.Id, OwnOutcome(kind, code));
                _log.Ended(facts.Id, kind, code, null);
            }
        }
    }

    // Writes refused, then, for a command with an identity, the file of its Refused outcome with the code. signature and
    // form have no identity and write only the entry.
    public void Refuse(Guid? command, string? baseKey, OutpostModeEnum? mode, string code)
    {
        _log.Refused(command, baseKey, mode, code);

        if (command is Guid id)
        {
            _outbox.Put(id, OwnOutcome(OutpostAgentOutcomeKindEnum.Refused, code));
        }
    }

    // Writes the outcome file, then ended with the kind and the code that OutpostCodec.ReadOutcome reads from the bytes
    // for the command's mode, and the duration. The bytes are an outcome that the loop checked by §8.
    public void End(Guid command, ReadOnlySpan<byte> outcome, long durationMs)
    {
        // A form refused here is a defect of the loop, and its exception passes before anything is written.
        OutpostOutcomeMessage message = OutpostCodec.ReadOutcome(_log.Find(command)?.Mode, outcome);

        _outbox.Put(command, outcome);
        _log.Ended(command, message.Kind, message.Code, durationMs);
    }

    // For a command whose outcome file is of kind succeeded or failed, writes the file of Unknown result-too-large for a
    // Write or Failed result-too-large for a Read, then a second ended with it and the durationMs that Find gives.
    public void ReplaceForSize(Guid command)
    {
        AuditCommand facts = _log.Find(command)!;

        OutpostAgentOutcomeKindEnum kind = facts.Mode == OutpostModeEnum.Write
            ? OutpostAgentOutcomeKindEnum.Unknown
            : OutpostAgentOutcomeKindEnum.Failed;

        _outbox.Put(command, OwnOutcome(kind, OutpostOutcomeCodes.ResultTooLarge));
        _log.Ended(command, kind, OutpostOutcomeCodes.ResultTooLarge, facts.DurationMs);
    }

    // Writes confirmed, then removes the outcome file.
    public void Confirm(Guid command)
    {
        _log.Confirmed(command);
        _outbox.Remove(command);
    }

    private static byte[] OwnOutcome(OutpostAgentOutcomeKindEnum kind, string code)
    {
        return OutpostCodec.Write(new OutpostOutcomeMessage(kind, code, null, null, [], false));
    }

    // The outcome file of a command, read for the mode of its command, or for either mode when no entry names one, as
    // after a retention that removed its received.
    private OutpostOutcomeMessage ReadFile(AuditCommand facts)
    {
        byte[] bytes = _outbox.Read(facts.Id);

        try
        {
            return OutpostCodec.ReadOutcome(facts.Mode, bytes);
        }
        catch (OutpostProtocolException)
        {
            throw new AgentStopException($@"outcomes\{facts.Id}.json is not an outcome that the agent may send for its command");
        }
    }
}
