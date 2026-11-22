using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Checks;

// The checks of every command (§7 of docs/protocol.md, concept rule 1) and of the commands of a poll answer (§6.2),
// for one agent, the server key it pinned, its local policy and its clock. They record nothing: the loop records what
// they give.
internal sealed class CommandChecks
{
    private readonly Guid _agent;

    // The public key that the agent pinned at pairing; its owner disposes it.
    private readonly ECDsa _serverKey;

    private readonly AgentPolicy _policy;

    private readonly TimeProvider _clock;

    public CommandChecks(Guid agent, ECDsa serverKey, AgentPolicy policy, TimeProvider clock)
    {
        _agent = agent;
        _serverKey = serverKey;
        _policy = policy;
        _clock = clock;
    }

    // Follows §7: ReadCommand, whose signature and form give Refused without an identity; with an identity, Known when
    // Find knows it, whatever the rest of the bytes; then the codec's protocol-version and malformed, with the identity
    // alone; then not-addressed, not-served (no base of that key, or a Write where write is absent), clock-skew (issued
    // later than the clock plus skewMs) and expired (the clock later than deadline plus skewMs), with identity, base and
    // mode; otherwise Runs with the command.
    public CommandCheck Check(OutpostSignedCommand command, AuditLog log)
    {
        OutpostCommandMessage read;

        // The codec verifies the signature before it reads anything, then reads `protocol` and `id` before the rest:
        // steps 1 to 3 of §7. Only protocol-version and malformed name the command.
        try
        {
            read = OutpostCodec.ReadCommand(_serverKey, command);
        }
        catch (OutpostProtocolException refusal)
        {
            if (refusal.CommandId is not Guid id)
            {
                return new CommandCheck(CommandVerdictEnum.Refused, null, null, null, refusal.Code, null);
            }

            // Step 2: a known identity is decided before the version and the rest of the bytes.
            if (log.Find(id) is not null)
            {
                return Known(id);
            }

            return new CommandCheck(CommandVerdictEnum.Refused, id, null, null, refusal.Code, null);
        }

        if (log.Find(read.Id) is not null)
        {
            return Known(read.Id);
        }

        string? code = FirstFailingCheck(read);

        if (code is not null)
        {
            return new CommandCheck(CommandVerdictEnum.Refused, read.Id, read.Base, read.Mode, code, null);
        }

        return new CommandCheck(CommandVerdictEnum.Runs, read.Id, read.Base, read.Mode, null, read);
    }

    // Checks every command of an answer that ReadPollAnswer read, and gives null, the answer lost, when two commands
    // with an identity share it, whatever their verdicts, or when those that Runs exceed, for a base and mode, the free
    // that the poll declared; otherwise the checks in the order of the answer. "Whatever their verdicts": the server
    // never sends two, and one comparison keeps "never twice from one answer" (§6.2).
    public IReadOnlyList<CommandCheck>? CheckAnswer(OutpostPollRequest poll, OutpostPollAnswer answer, AuditLog log)
    {
        List<CommandCheck> checks = [];
        HashSet<Guid> identities = [];
        Dictionary<(string Base, OutpostModeEnum Mode), int> running = [];

        foreach (OutpostSignedCommand command in answer.Commands)
        {
            CommandCheck check = Check(command, log);

            // Signature and form name no command, so two of them are never two commands of one identity.
            if (check.Id is Guid id && !identities.Add(id))
            {
                return null;
            }

            // A command that runs is counted against the free workers of its base and mode; a refused or known one
            // counts for nothing.
            if (check.Command is OutpostCommandMessage runs)
            {
                (string Base, OutpostModeEnum Mode) mode = (runs.Base, runs.Mode);
                running[mode] = running.GetValueOrDefault(mode) + 1;

                if (running[mode] > FreeOf(poll, runs.Base, runs.Mode))
                {
                    return null;
                }
            }

            checks.Add(check);
        }

        return checks;
    }

    private static CommandCheck Known(Guid id)
    {
        return new CommandCheck(CommandVerdictEnum.Known, id, null, null, null, null);
    }

    // The free workers that the poll declared for a base and mode, 0 for a base or mode it did not declare.
    private static int FreeOf(OutpostPollRequest poll, string baseKey, OutpostModeEnum mode)
    {
        OutpostPollBase? declared = poll.Bases.FirstOrDefault(pollBase => pollBase.Key == baseKey);
        OutpostPollMode? declaredMode = mode == OutpostModeEnum.Read ? declared?.Read : declared?.Write;

        return declaredMode?.Free ?? 0;
    }

    // Step 4 of §7, in its order: the code of the first check that fails, or null when every check holds. The clock is
    // judged with the skew that the command carries, not with a setting of the agent (rule 1).
    private string? FirstFailingCheck(OutpostCommandMessage command)
    {
        if (command.Agent != _agent)
        {
            return OutpostOutcomeCodes.NotAddressed;
        }

        AgentBasePolicy? served = _policy.Bases.FirstOrDefault(policyBase => policyBase.Key == command.Base);

        if (served is null || (command.Mode == OutpostModeEnum.Write && served.Write is null))
        {
            return OutpostOutcomeCodes.NotServed;
        }

        DateTimeOffset now = _clock.GetUtcNow();

        if (command.Issued > now + command.Skew)
        {
            return OutpostOutcomeCodes.ClockSkew;
        }

        if (now > command.Deadline + command.Skew)
        {
            return OutpostOutcomeCodes.Expired;
        }

        return null;
    }
}
