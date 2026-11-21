namespace Moltaro.OnecOutpost.Server.Store;

// The operations of the store on PostgreSQL. Each opens a context of its own, runs under ReadCommitted and holds no lock
// while it waits for anything. Every time it writes or compares comes from its TimeProvider in UTC, never from the
// clock of the database (PQ3).
internal sealed class OutpostStore
{
    private readonly string _connectionString;

    private readonly string _schema;

    private readonly TimeProvider _timeProvider;

    public OutpostStore(string connectionString, string schema, TimeProvider timeProvider)
    {
        _connectionString = connectionString;
        _schema = schema;
        _timeProvider = timeProvider;
    }

    // Writes the agent as Pending, with its name and the node's time, and its token with the secret and the expiry, in
    // one short transaction.
    public Task CreatePairingTokenAsync(
        Guid agentId,
        string agentName,
        byte[] secret,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    // The secret of the agent's token when the token has not expired and the agent is Pending; null otherwise.
    public Task<byte[]?> FindPairingSecretAsync(Guid agentId, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    // In one statement: deletes the token when it has not expired and makes its agent, while Pending, Paired with the key
    // and the time. True for the one call that made the agent Paired.
    public Task<bool> RedeemPairingTokenAsync(Guid agentId, byte[] publicKey, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    // In one statement: deletes the tokens that expired and their agents that are still Pending. Gives the number of
    // tokens.
    public Task<int> PurgePairingTokensAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
