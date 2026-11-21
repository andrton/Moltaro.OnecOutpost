using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Moltaro.OnecOutpost.Protocol.Messages;
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

    // The row of the agent, or null when there is none.
    public async Task<AgentRow?> FindAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        AgentRow? agent = await context.Agents
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == agentId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return agent;
    }

    // Records a poll that is newer than the newest recorded, while the agent is Paired, in one short transaction: its
    // number, the node's time, the end of its lease (§6.2) and the agent version; the declared bases in place of the
    // agent's rows; and a claim of each declared key that no agent holds (rule 14). Newest is null then. An older poll
    // changes nothing (rule 10) and gets the recorded number with the lists of the newest poll.
    //
    // The order of locks is a pairing token, then an agent row, then claims, in every operation. The update of the agent
    // row holds that row until the commit, so the polls of one agent, its revocation and the releases of its keys wait
    // for each other on it. The claims are inserted in ordinal order of their keys, so two polls that claim the same
    // keys never wait for each other in a cycle.
    public async Task<PollState> RecordPollAsync(
        Guid agentId,
        OutpostPollRequest poll,
        TimeSpan hold,
        TimeSpan leaseGrace,
        TimeSpan clockSkew,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        // §6.2: the lease ends at the earlier of the recording and the signed time plus the skew, each plus the hold
        // and the grace. The signed time is compared with the node's time less the skew, so that no time an agent
        // signs, however far ahead, overflows.
        DateTimeOffset leaseFrom = poll.Time < now - clockSkew ? poll.Time + clockSkew : now;
        DateTimeOffset leaseEndsAt = leaseFrom + hold + leaseGrace;

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // Rule 10: a poll whose number is not above the newest recorded is older, an exact replay included.
        int recorded = await context.Agents
            .Where(agent => agent.Id == agentId
                && agent.State == OutpostAgentStateEnum.Paired
                && (agent.NewestPoll == null || agent.NewestPoll < poll.Number))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(agent => agent.NewestPoll, poll.Number)
                    .SetProperty(agent => agent.LastPollAt, now)
                    .SetProperty(agent => agent.LeaseEndsAt, leaseEndsAt)
                    .SetProperty(agent => agent.AgentVersion, poll.AgentVersion),
                cancellationToken);

        if (recorded == 0)
        {
            PollState older = await ReadStateAsync(context, agentId, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return older;
        }

        await context.AgentBases.Where(agentBase => agentBase.AgentId == agentId).ExecuteDeleteAsync(cancellationToken);

        // Every declared base opens Read in v1; Write is open when the poll declares it (§6.2).
        context.AgentBases.AddRange(poll.Bases.Select(pollBase => new AgentBaseRow
        {
            AgentId = agentId,
            Key = pollBase.Key,
            WriteOpen = pollBase.Write is not null,
        }));

        await context.SaveChangesAsync(cancellationToken);

        await ClaimAsync(context, agentId, poll.Bases.Select(pollBase => pollBase.Key), cancellationToken);

        // A statement of its own, so that it sees a claim that another poll committed while this one waited for it.
        PollState newest = await ReadStateAsync(context, agentId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return newest with { Newest = null };
    }

    // The state of the agent for the answer to its poll of this number, in one statement that changes nothing and takes
    // no lock. Newest is the recorded number when it is not this one, and null when it is.
    public async Task<PollState> ReadPollStateAsync(Guid agentId, long number, CancellationToken cancellationToken)
    {
        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        PollState state = await ReadStateAsync(context, agentId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return state.Newest == number ? state with { Newest = null } : state;
    }

    // Every agent, by its creation and identity, with the bases of its newest poll by key and whether it holds each, in
    // one statement. An agent is connected while it is Paired and its lease ends later than the store's now.
    public async Task<IReadOnlyList<OutpostAgent>> ListAgentsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // One row per base of each agent, with the holder of its key, or one row without a base for an agent that
        // declared none. The rows of an agent come together, in the order of the list.
        var rows = await (
            from agent in context.Agents
            join agentBase in context.AgentBases on agent.Id equals agentBase.AgentId into agentBases
            from agentBase in agentBases.DefaultIfEmpty()
            join claim in context.BaseClaims on agentBase!.Key equals claim.Key into claims
            from claim in claims.DefaultIfEmpty()
            orderby agent.CreatedAt, agent.Id
            select new
            {
                Agent = agent,
                Key = agentBase != null ? agentBase.Key : null,
                WriteOpen = agentBase != null && agentBase.WriteOpen,
                Holder = claim != null ? claim.AgentId : (Guid?)null,
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        List<OutpostAgent> agents = [];

        foreach (var agentRows in rows.GroupBy(row => row.Agent.Id))
        {
            AgentRow agent = agentRows.First().Agent;

            // A base is open for Read whenever it is declared (§6.2).
            OutpostBase[] bases =
            [
                .. agentRows
                    .Where(row => row.Key is not null)
                    .Select(row => new OutpostBase(row.Key!, true, row.WriteOpen, row.Holder == agent.Id))
                    .OrderBy(agentBase => agentBase.Key, StringComparer.Ordinal),
            ];

            bool connected = agent.State == OutpostAgentStateEnum.Paired && agent.LeaseEndsAt > now;

            agents.Add(new OutpostAgent(
                agent.Id,
                agent.Name,
                agent.State,
                agent.CreatedAt,
                agent.PairedAt,
                agent.LastPollAt,
                connected,
                agent.AgentVersion,
                bases));
        }

        return agents;
    }

    // Deletes the agent's token, makes it Revoked and frees its keys, in one short transaction in the order of locks.
    // True when the call made the agent Revoked. Its bases stay, so the list shows them, none held.
    //
    // A poll of the agent that commits first has its claims deleted here, since this statement waits for the agent row
    // and the deletion of the claims comes after it; a poll that comes later finds the agent Revoked and claims nothing.
    public async Task<bool> RevokeAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        await context.PairingTokens.Where(token => token.AgentId == agentId).ExecuteDeleteAsync(cancellationToken);

        int revoked = await context.Agents
            .Where(agent => agent.Id == agentId && agent.State != OutpostAgentStateEnum.Revoked)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(agent => agent.State, OutpostAgentStateEnum.Revoked),
                cancellationToken);

        await context.BaseClaims.Where(claim => claim.AgentId == agentId).ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return revoked == 1;
    }

    // Frees a key whose holder's newest poll no longer declares it, in one short transaction (rule 14).
    //
    // The holder's agent row is locked before its bases are read, so that a poll of the holder that is committing is
    // seen whole. FOR NO KEY UPDATE conflicts with the update of a poll, and not with the key-share locks of the rows that
    // refer to the agent. Another agent may claim the key between the read of the holder and the lock, after a release or
    // a revocation; the call then answers StillDeclared and changes nothing, and the next call decides.
    public async Task<OutpostReleaseResultEnum> ReleaseBaseAsync(string baseKey, CancellationToken cancellationToken)
    {
        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        string agents = TableName(context, context.Agents.EntityType);
        string lockHolder = $"SELECT id FROM {agents} WHERE id = @agent_id FOR NO KEY UPDATE";

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        Guid? holder = await context.BaseClaims
            .Where(claim => claim.Key == baseKey)
            .Select(claim => (Guid?)claim.AgentId)
            .SingleOrDefaultAsync(cancellationToken);

        if (holder is not Guid holderId)
        {
            await transaction.CommitAsync(cancellationToken);

            return OutpostReleaseResultEnum.NotHeld;
        }

        await context.Database.ExecuteSqlRawAsync(
            lockHolder,
            [new NpgsqlParameter<Guid>("agent_id", holderId)],
            cancellationToken);

        int released = await context.BaseClaims
            .Where(claim => claim.Key == baseKey
                && claim.AgentId == holderId
                && !context.AgentBases.Any(agentBase => agentBase.AgentId == holderId && agentBase.Key == baseKey))
            .ExecuteDeleteAsync(cancellationToken);

        bool stillClaimed = released == 0
            && await context.BaseClaims.AnyAsync(claim => claim.Key == baseKey, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        if (released == 1)
        {
            return OutpostReleaseResultEnum.Released;
        }

        return stillClaimed ? OutpostReleaseResultEnum.StillDeclared : OutpostReleaseResultEnum.NotHeld;
    }

    // Claims each key that no agent holds for the agent, in one statement, in ordinal order of the keys (rule 14). Of two
    // polls that claim one key at once, the second waits for the first and then claims nothing.
    private static async Task ClaimAsync(
        StoreContext context,
        Guid agentId,
        IEnumerable<string> keys,
        CancellationToken cancellationToken)
    {
        string claims = TableName(context, context.BaseClaims.EntityType);

        string sql = $"""
            INSERT INTO {claims} (key, agent_id)
            SELECT declared.key, @agent_id
            FROM unnest(@keys) AS declared (key)
            ORDER BY declared.key COLLATE "C"
            ON CONFLICT (key) DO NOTHING
            """;

        NpgsqlParameter[] parameters =
        [
            new NpgsqlParameter<Guid>("agent_id", agentId),
            new NpgsqlParameter<string[]>("keys", [.. keys]),
        ];

        await context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
    }

    // The state of the agent in one statement: Revoked when it is not Paired, or not known; Newest the number of its
    // newest poll as recorded; and the keys of that poll, held when their claim names the agent and claimed otherwise,
    // a key that no agent holds included, each list in ordinal order.
    private static async Task<PollState> ReadStateAsync(
        StoreContext context,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from agent in context.Agents
            where agent.Id == agentId
            join agentBase in context.AgentBases on agent.Id equals agentBase.AgentId into agentBases
            from agentBase in agentBases.DefaultIfEmpty()
            join claim in context.BaseClaims on agentBase!.Key equals claim.Key into claims
            from claim in claims.DefaultIfEmpty()
            select new
            {
                agent.State,
                agent.NewestPoll,
                Key = agentBase != null ? agentBase.Key : null,
                Holder = claim != null ? claim.AgentId : (Guid?)null,
            })
            .ToListAsync(cancellationToken);

        bool paired = rows.Count > 0 && rows[0].State == OutpostAgentStateEnum.Paired;
        long? newest = rows.Count > 0 ? rows[0].NewestPoll : null;

        List<string> held = [];
        List<string> claimed = [];

        foreach (var row in rows.Where(row => row.Key is not null))
        {
            List<string> list = row.Holder == agentId ? held : claimed;
            list.Add(row.Key!);
        }

        held.Sort(StringComparer.Ordinal);
        claimed.Sort(StringComparer.Ordinal);

        return new PollState(!paired, newest, held, claimed);
    }

    // The table of an entity type, with its schema, as the model names it, delimited for raw SQL.
    private static string TableName(StoreContext context, IEntityType entityType)
    {
        ISqlGenerationHelper sqlGenerationHelper = context.GetService<ISqlGenerationHelper>();

        return sqlGenerationHelper.DelimitIdentifier(entityType.GetTableName()!, entityType.GetSchema());
    }
}
