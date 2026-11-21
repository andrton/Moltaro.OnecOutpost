using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Moltaro.OnecOutpost.Server.Store;

// The operations of the store on PostgreSQL. Each opens a context of its own, runs under ReadCommitted and holds no lock
// while it waits for anything. Every time it writes or compares comes from its TimeProvider in UTC, never from the
// clock of the database (PQ3).
//
// Each operation is one transaction whose isolation it sets itself, so that it does not depend on the default of the
// database. Under ReadCommitted, a statement that waited for a row another transaction changed reads that row again as
// committed and checks its conditions anew; the statements below rely on that to give the one change to one caller.
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
    public async Task CreatePairingTokenAsync(
        Guid agentId,
        string agentName,
        byte[] secret,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        context.Agents.Add(new AgentRow
        {
            Id = agentId,
            Name = agentName,
            State = OutpostAgentStateEnum.Pending,
            CreatedAt = _timeProvider.GetUtcNow(),
        });

        context.PairingTokens.Add(new PairingTokenRow
        {
            AgentId = agentId,
            Secret = secret,
            ExpiresAt = expiresAt,
        });

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    // The secret of the agent's token when the token has not expired and the agent is Pending; null otherwise. A token is
    // expired from its expiry on.
    public async Task<byte[]?> FindPairingSecretAsync(Guid agentId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        byte[]? secret = await (
            from token in context.PairingTokens
            join agent in context.Agents on token.AgentId equals agent.Id
            where token.AgentId == agentId
                && token.ExpiresAt > now
                && agent.State == OutpostAgentStateEnum.Pending
            select token.Secret)
            .SingleOrDefaultAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return secret;
    }

    // In one statement: deletes the token when it has not expired and makes its agent, while Pending, Paired with the key
    // and the time. True for the one call that made the agent Paired.
    //
    // The token is deleted first. Of two redemptions of one token, the second waits for the row that the first deleted,
    // finds it gone once the first commits, and so changes nothing: its update has no token to join.
    public async Task<bool> RedeemPairingTokenAsync(Guid agentId, byte[] publicKey, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        string agents = TableName(context, context.Agents.EntityType);
        string tokens = TableName(context, context.PairingTokens.EntityType);

        string sql = $"""
            WITH redeemed AS (
                DELETE FROM {tokens}
                WHERE agent_id = @agent_id AND expires_at > @now
                RETURNING agent_id
            )
            UPDATE {agents} AS agent
            SET state = @paired, public_key = @public_key, paired_at = @now
            FROM redeemed
            WHERE agent.id = redeemed.agent_id AND agent.state = @pending
            """;

        NpgsqlParameter[] parameters =
        [
            new NpgsqlParameter<Guid>("agent_id", agentId),
            new NpgsqlParameter<DateTimeOffset>("now", now),
            new NpgsqlParameter<byte[]>("public_key", publicKey),
            new NpgsqlParameter<int>("paired", (int)OutpostAgentStateEnum.Paired),
            new NpgsqlParameter<int>("pending", (int)OutpostAgentStateEnum.Pending),
        ];

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        int paired = await context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return paired == 1;
    }

    // In one statement: deletes the tokens that expired and their agents that are still Pending. Gives the number of
    // tokens. A token is expired from its expiry on. Tokens are few, so the statement takes them all at once.
    //
    // A redemption that races the purge on one token waits for it or makes the purge wait: whichever deletes the token
    // first wins, and the other finds it gone. Purges on several nodes at once remove each token once.
    public async Task<int> PurgePairingTokensAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        string agents = TableName(context, context.Agents.EntityType);
        string tokens = TableName(context, context.PairingTokens.EntityType);

        // EF Core reads the count from the column Value. The statement is sent as it is: PostgreSQL allows a WITH that
        // changes rows only at the top of a statement, so nothing may be composed on it.
        string sql = $"""
            WITH expired AS (
                DELETE FROM {tokens}
                WHERE expires_at <= @now
                RETURNING agent_id
            ),
            removed AS (
                DELETE FROM {agents}
                WHERE id IN (SELECT agent_id FROM expired) AND state = @pending
            )
            SELECT count(*)::integer AS "Value" FROM expired
            """;

        NpgsqlParameter[] parameters =
        [
            new NpgsqlParameter<DateTimeOffset>("now", now),
            new NpgsqlParameter<int>("pending", (int)OutpostAgentStateEnum.Pending),
        ];

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        List<int> purged = await context.Database.SqlQueryRaw<int>(sql, parameters).ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return purged.Single();
    }

    // The table of an entity type, with its schema, as the model names it, delimited for raw SQL.
    private static string TableName(StoreContext context, IEntityType entityType)
    {
        ISqlGenerationHelper sqlGenerationHelper = context.GetService<ISqlGenerationHelper>();

        return sqlGenerationHelper.DelimitIdentifier(entityType.GetTableName()!, entityType.GetSchema());
    }
}
