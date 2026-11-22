using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Protocol.Messages;

namespace Moltaro.OnecOutpost.Agent.Checks;

// The checks of every command (§7 of docs/protocol.md, concept rule 1) and of the commands of a poll answer (§6.2),
// for one agent, the server key it pinned, its local policy and its clock. They record nothing: the loop records what
// they give.
internal sealed class CommandChecks
{
    public CommandChecks(Guid agent, ECDsa serverKey, AgentPolicy policy, TimeProvider clock)
    {
        throw new NotSupportedException();
    }

    // Follows §7: ReadCommand, whose signature and form give Refused without an identity; with an identity, Known when
    // Find knows it, whatever the rest of the bytes; then the codec's protocol-version and malformed, with the identity
    // alone; then not-addressed, not-served (no base of that key, or a Write where write is absent), clock-skew (issued
    // later than the clock plus skewMs) and expired (the clock later than deadline plus skewMs), with identity, base and
    // mode; otherwise Runs with the command.
    public CommandCheck Check(OutpostSignedCommand command, AuditLog log)
    {
        throw new NotSupportedException();
    }

    // Checks every command of an answer that ReadPollAnswer read, and gives null, the answer lost, when two commands
    // with an identity share it, whatever their verdicts, or when those that Runs exceed, for a base and mode, the free
    // that the poll declared; otherwise the checks in the order of the answer. "Whatever their verdicts": the server
    // never sends two, and one comparison keeps "never twice from one answer" (§6.2).
    public IReadOnlyList<CommandCheck>? CheckAnswer(OutpostPollRequest poll, OutpostPollAnswer answer, AuditLog log)
    {
        throw new NotSupportedException();
    }
}
