namespace Moltaro.OnecOutpost.Server.Store;

// A row of the table agents: one agent, from the pairing token that created it as Pending to its pairing and its
// revocation.
internal sealed class AgentRow
{
    // The identity of the agent, which its pairing token carries.
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public OutpostAgentStateEnum State { get; set; }

    // Written in UTC from the store's TimeProvider, never from the clock of the database (PQ3).
    public DateTimeOffset CreatedAt { get; set; }

    // Null while the agent is Pending.
    public DateTimeOffset? PairedAt { get; set; }

    // The public key the agent gave at its pairing; null while it is Pending.
    public byte[]? PublicKey { get; set; }

    // The number of the newest poll recorded; null until the agent's first poll, as are the three below.
    public long? NewestPoll { get; set; }

    // The moment the node recorded the newest poll, from the store's TimeProvider (PQ3).
    public DateTimeOffset? LastPollAt { get; set; }

    // The end of the lease of the newest poll, computed once when it was recorded (§6.2), so that the agent is connected
    // while this is later than the store's now.
    public DateTimeOffset? LeaseEndsAt { get; set; }

    // The version of the agent program, from its newest poll.
    public string? AgentVersion { get; set; }
}
