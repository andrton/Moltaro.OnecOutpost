namespace Moltaro.OnecOutpost.Agent.Policy;

// The local policy, as AgentPolicyReader read it from policy.json, with the defaults applied: how long the audit log
// is kept, the proxy to the server or null to connect directly, the longest delay between attempts after an error,
// the agent's wait for an answer to a pair or an outcome and for the body of a poll answer, and at least one base,
// with unique keys.
internal sealed record AgentPolicy(
    int AuditRetentionDays,
    Uri? Proxy,
    TimeSpan RetryDelayMax,
    TimeSpan RequestTimeout,
    IReadOnlyList<AgentBasePolicy> Bases)
{
    // A tenth of RetryDelayMax: the first delay after an error, and the shortest.
    public TimeSpan FirstRetryDelay => RetryDelayMax / 10;

    // The delay after another error: twice the one before, from FirstRetryDelay up to RetryDelayMax, so 3, 6, 12, 24,
    // 30, 30 … seconds by default (concept §4, §5).
    public TimeSpan NextRetryDelay(TimeSpan before)
    {
        long ticks = Math.Clamp(before.Ticks * 2, FirstRetryDelay.Ticks, RetryDelayMax.Ticks);

        return TimeSpan.FromTicks(ticks);
    }
}
