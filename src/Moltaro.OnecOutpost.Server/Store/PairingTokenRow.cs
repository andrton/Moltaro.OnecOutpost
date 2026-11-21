namespace Moltaro.OnecOutpost.Server.Store;

// A row of the table pairing_tokens: the token of one Pending agent, keyed by that agent and deleted with it. The
// redemption deletes the row, so the secret is gone with the token.
internal sealed class PairingTokenRow
{
    public Guid AgentId { get; set; }

    public byte[] Secret { get; set; } = null!;

    // Written in UTC from the store's TimeProvider, never from the clock of the database (PQ3).
    public DateTimeOffset ExpiresAt { get; set; }
}
