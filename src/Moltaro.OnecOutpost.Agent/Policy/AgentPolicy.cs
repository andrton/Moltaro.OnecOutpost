namespace Moltaro.OnecOutpost.Agent.Policy;

// The local policy, as AgentPolicyReader read it from policy.json, with the defaults applied: how long the audit log
// is kept, the proxy to the server or null to connect directly, the longest delay between attempts after an error,
// and at least one base, with unique keys.
internal sealed record AgentPolicy(
    int AuditRetentionDays,
    Uri? Proxy,
    TimeSpan RetryDelayMax,
    IReadOnlyList<AgentBasePolicy> Bases);
