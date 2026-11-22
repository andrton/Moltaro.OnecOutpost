using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Polls;
using Moltaro.OnecOutpost.Agent.Records;
using Moltaro.OnecOutpost.Agent.Secrets;

namespace Moltaro.OnecOutpost.Agent.Start;

// What a start that passed gives the run: the policy, the pairing, the secrets, the audit log, whose lock the caller
// releases by disposing it, the outbox, the records of commands and the poll numbers of the identity.
internal sealed record AgentRun(
    AgentPolicy Policy,
    AgentPairingSettings Pairing,
    AgentSecrets Secrets,
    AuditLog Log,
    Outbox Outbox,
    CommandRecords Records,
    PollNumbers Numbers);
