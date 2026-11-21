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
}
