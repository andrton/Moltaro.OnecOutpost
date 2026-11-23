using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
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
    // The moment at which time ends a command with no outcome (rules 7, 11), over the aliases command and agent and
    // the parameter @waiting: its take deadline while no poll took it, by the clock of the node; once taken, the end of
    // its agent's lease plus the run limit of its last hand-out.
    private const string DueAt =
        "CASE WHEN command.phase = @waiting THEN command.deadline ELSE agent.lease_ends_at + command.run_limit END";

    // An outcome that the server set, of the kind Unknown or Failed, over the parameters @unknown and @failed. It is
    // the one outcome that a later fact replaces (rule 11): the outcome of the agent, or Expired by a poll that proves
    // the command never reached its agent.
    private const string Replaceable = "(outcome IS NULL AND outcome_kind IN (@unknown, @failed))";

    // The JSON of an entry of `commands` in a poll answer around the base64url of its bytes and of its signature (§6.2).
    private const string EntryJson = """{"bytes":"","signature":""}""";

    // The most commands that one statement of the purge deletes.
    private const int PurgeBatch = 1_000;

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
    // agent's rows; a claim of each declared key that no agent holds (rule 14); and its list of received commands
    // (rules 7, 10, 11). Newest is null then, and Expired lists the commands that the list made Expired. An older poll
    // changes nothing (rule 10) and gets the recorded number with the lists of the newest poll, and no Expired.
    //
    // The order of locks is a pairing token, then an agent row, then claims, then commands, in every operation. The
    // update of the agent row holds that row until the commit, so the polls of one agent, its takes, its revocation
    // and the releases of its keys wait for each other on it. A create of one of its commands holds the row FOR SHARE:
    // it waits for each of those and each of them for it, but two creates do not wait for each other, since FOR SHARE
    // locks do not conflict. The claims are inserted in ordinal order of their keys, so two polls that claim the same
    // keys never wait for each other in a cycle.
    public async Task<PollRecord> RecordPollAsync(
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

            return new PollRecord(older, []);
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

        IReadOnlyList<Guid> expired = await ReconcileReceivedAsync(context, agentId, poll, now, cancellationToken);

        // A statement of its own, so that it sees a claim that another poll committed while this one waited for it.
        PollState newest = await ReadStateAsync(context, agentId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new PollRecord(newest with { Newest = null }, expired);
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

        return ForPoll(state, number);
    }

    // Every agent, by its creation and identity, with the bases of its newest poll by key and whether it holds each, in
    // one statement, and whether it is connected (IsConnected).
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

            bool connected = IsConnected(agent.State, agent.LeaseEndsAt, now);

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

    // Deletes the agent's token, makes it Revoked, frees its keys and ends its commands with no outcome, in one short
    // transaction in the order of locks. True when the call made the agent Revoked, and only then are commands ended:
    // those that no poll took as Refused `revoked`, the taken ones as rule 3 says, Unknown `revoked` for a Write and
    // Failed `lost` for a Read (rule 13). Its bases stay, so the list shows them, none held.
    //
    // A poll of the agent that commits first has its claims deleted here, since this statement waits for the agent row
    // and the deletion of the claims comes after it; a poll that comes later finds the agent Revoked and claims nothing.
    // A create of a command holds the agent row FOR SHARE, so it either commits first and its command is ended here,
    // or finds the agent Revoked and creates nothing.
    public async Task<Revocation> RevokeAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        string commands = TableName(context, context.Commands.EntityType);

        string refuseWaiting = $"""
            UPDATE {commands}
            SET outcome_kind = @refused, outcome_code = @revoked, ended_at = @now
            WHERE agent_id = @agent_id AND phase = @waiting AND outcome_kind IS NULL
            RETURNING id AS "Value"
            """;

        string endTaken = $"""
            UPDATE {commands}
            SET outcome_kind = CASE WHEN mode = @write THEN @unknown ELSE @failed END,
                outcome_code = CASE WHEN mode = @write THEN @revoked ELSE @lost END,
                ended_at = @now
            WHERE agent_id = @agent_id AND phase <> @waiting AND outcome_kind IS NULL
            RETURNING id AS "Value"
            """;

        NpgsqlParameter[] refuseParameters =
        [
            new NpgsqlParameter<Guid>("agent_id", agentId),
            new NpgsqlParameter<DateTimeOffset>("now", now),
            new NpgsqlParameter<int>("waiting", (int)CommandPhaseEnum.Waiting),
            new NpgsqlParameter<int>("refused", (int)OutpostOutcomeKindEnum.Refused),
            new NpgsqlParameter<string>("revoked", OutpostOutcomeCodes.Revoked),
        ];

        NpgsqlParameter[] endParameters =
        [
            new NpgsqlParameter<Guid>("agent_id", agentId),
            new NpgsqlParameter<DateTimeOffset>("now", now),
            new NpgsqlParameter<int>("waiting", (int)CommandPhaseEnum.Waiting),
            new NpgsqlParameter<int>("write", (int)OutpostModeEnum.Write),
            new NpgsqlParameter<int>("unknown", (int)OutpostOutcomeKindEnum.Unknown),
            new NpgsqlParameter<int>("failed", (int)OutpostOutcomeKindEnum.Failed),
            new NpgsqlParameter<string>("revoked", OutpostOutcomeCodes.Revoked),
            new NpgsqlParameter<string>("lost", OutpostOutcomeCodes.Lost),
        ];

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        await context.PairingTokens.Where(token => token.AgentId == agentId).ExecuteDeleteAsync(cancellationToken);

        int revoked = await context.Agents
            .Where(agent => agent.Id == agentId && agent.State != OutpostAgentStateEnum.Revoked)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(agent => agent.State, OutpostAgentStateEnum.Revoked),
                cancellationToken);

        await context.BaseClaims.Where(claim => claim.AgentId == agentId).ExecuteDeleteAsync(cancellationToken);

        if (revoked == 0)
        {
            await transaction.CommitAsync(cancellationToken);

            return new Revocation(false, []);
        }

        List<Guid> refused = await context.Database
            .SqlQueryRaw<Guid>(refuseWaiting, refuseParameters)
            .ToListAsync(cancellationToken);

        List<Guid> ended = await context.Database
            .SqlQueryRaw<Guid>(endTaken, endParameters)
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new Revocation(true, [.. refused, .. ended]);
    }

    // Frees a key whose holder's newest poll no longer declares it, in one short transaction (rule 14).
    //
    // The holder's agent row is locked before its bases are read, so that a poll of the holder that is committing is
    // seen whole. FOR NO KEY UPDATE conflicts with the update of a poll and with the FOR SHARE of a create of the
    // holder's commands, so a release and those wait for each other; it does not conflict with the key-share locks that
    // the inserts of rows that refer to the agent take. Another agent may claim the key between the read of the holder
    // and the lock, after a release or a revocation; the call then answers StillDeclared and changes nothing, and the
    // next call decides.
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

    // The take of a poll, in one short transaction: while the agent is Paired and the poll its newest, under the lock of
    // the agent row, at most `free` commands of each mode of the poll, oldest first, skipping rows another transaction
    // holds, while their entries fit `room`, the first always; they become HandedOut with the run limit of their mode
    // (rules 8, 10, 11, 14). The state of the poll is read in the same transaction.
    //
    // A command is taken while it has no outcome, is Waiting or HandedOut, its deadline is later than the node's now
    // (rule 7), its mode is one the poll declares and its key one the agent holds (rule 14). A poll that takes anything
    // answers and takes no more, so a HandedOut command that a take finds was handed out by an older poll and is
    // missing from this poll's list: it is handed out again (rule 11). The lock of the agent row makes two takes of one
    // agent wait for each other, and keeps the agent's claims as they are until the commit, since a release and a
    // revocation lock that row too.
    public async Task<PollTake> TakeCommandsAsync(
        Guid agentId,
        OutpostPollRequest poll,
        long room,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        // Each mode of each base of the poll, with its workers. A mode with no free worker takes nothing, by the limit
        // of its pick.
        List<(string Key, OutpostModeEnum Mode, OutpostPollMode Workers)> modes = [];

        foreach (OutpostPollBase pollBase in poll.Bases)
        {
            modes.Add((pollBase.Key, OutpostModeEnum.Read, pollBase.Read));

            if (pollBase.Write is OutpostPollMode write)
            {
                modes.Add((pollBase.Key, OutpostModeEnum.Write, write));
            }
        }

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        string agents = TableName(context, context.Agents.EntityType);
        string claims = TableName(context, context.BaseClaims.EntityType);
        string commands = TableName(context, context.Commands.EntityType);

        string lockAgent = $"""
            SELECT id AS "Value" FROM {agents}
            WHERE id = @agent_id AND state = @paired AND newest_poll = @number
            FOR NO KEY UPDATE
            """;

        // The entry of a command in the answer is the base64url of its bytes and of its signature, without padding,
        // inside the JSON of EntryJson, with a comma before every entry but the first (§6.2). The entries are summed in
        // the order of the answer, so the first that does not fit stops the take, whatever follows it.
        string take = $"""
            WITH picked AS (
                SELECT command.id, command.sequence, command.entry_length, wanted.run_limit
                FROM unnest(@keys, @modes, @frees, @run_limits) AS wanted (base_key, mode, free, run_limit)
                JOIN {claims} AS claim ON claim.key = wanted.base_key AND claim.agent_id = @agent_id
                CROSS JOIN LATERAL (
                    SELECT candidate.id, candidate.sequence,
                        (4 * octet_length(candidate.bytes)::bigint + 2) / 3
                            + (4 * octet_length(candidate.signature)::bigint + 2) / 3
                            + @entry_json AS entry_length
                    FROM {commands} AS candidate
                    WHERE candidate.agent_id = @agent_id
                        AND candidate.base_key = wanted.base_key
                        AND candidate.mode = wanted.mode
                        AND candidate.phase IN (@waiting, @handed_out)
                        AND candidate.outcome_kind IS NULL
                        AND candidate.deadline > @now
                    ORDER BY candidate.sequence
                    LIMIT wanted.free
                    FOR UPDATE SKIP LOCKED
                ) AS command
            ),
            kept AS (
                SELECT id, run_limit
                FROM (
                    SELECT id, run_limit,
                        row_number() OVER answer AS position,
                        sum(entry_length) OVER answer + row_number() OVER answer - 1 AS filled
                    FROM picked
                    WINDOW answer AS (ORDER BY sequence)
                ) AS ranked
                WHERE position = 1 OR filled <= @room
            ),
            handed AS (
                UPDATE {commands} AS command
                SET phase = @handed_out, run_limit = kept.run_limit
                FROM kept
                WHERE command.id = kept.id
                RETURNING command.sequence, command.bytes, command.signature
            )
            SELECT bytes AS "Bytes", signature AS "Signature" FROM handed ORDER BY sequence
            """;

        NpgsqlParameter[] lockParameters =
        [
            new NpgsqlParameter<Guid>("agent_id", agentId),
            new NpgsqlParameter<int>("paired", (int)OutpostAgentStateEnum.Paired),
            new NpgsqlParameter<long>("number", poll.Number),
        ];

        NpgsqlParameter[] takeParameters =
        [
            new NpgsqlParameter<Guid>("agent_id", agentId),
            new NpgsqlParameter<DateTimeOffset>("now", now),
            new NpgsqlParameter<long>("room", room),
            new NpgsqlParameter<int>("entry_json", EntryJson.Length),
            new NpgsqlParameter<int>("waiting", (int)CommandPhaseEnum.Waiting),
            new NpgsqlParameter<int>("handed_out", (int)CommandPhaseEnum.HandedOut),
            new NpgsqlParameter<string[]>("keys", [.. modes.Select(mode => mode.Key)]),
            new NpgsqlParameter<int[]>("modes", [.. modes.Select(mode => (int)mode.Mode)]),
            new NpgsqlParameter<int[]>("frees", [.. modes.Select(mode => mode.Workers.Free)]),
            new NpgsqlParameter<TimeSpan[]>("run_limits", [.. modes.Select(mode => mode.Workers.RunLimit)]),
        ];

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        List<Guid> locked = await context.Database
            .SqlQueryRaw<Guid>(lockAgent, lockParameters)
            .ToListAsync(cancellationToken);

        List<OutpostSignedCommand> handed = [];

        if (locked.Count == 1)
        {
            handed = await context.Database
                .SqlQueryRaw<OutpostSignedCommand>(take, takeParameters)
                .ToListAsync(cancellationToken);
        }

        // Revoked, or a Newest that is not null, says why a take that found no row took nothing.
        PollState state = await ReadStateAsync(context, agentId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new PollTake(ForPoll(state, poll.Number), handed);
    }

    // The command that holds the base and caller key, in one short transaction that first makes it Expired when it is
    // Waiting with no outcome and its deadline has passed; null when none holds it (rule 4).
    //
    // Same compares the mode, the script exactly and the parameters as JSON values with those of the command, which
    // are read from its bytes. The parameters are the text the call wrote, which holds no name twice.
    public async Task<CommandMatch?> FindCallerKeyAsync(
        string baseKey,
        string callerKey,
        OutpostModeEnum mode,
        string script,
        string parameters,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        string commands = TableName(context, context.Commands.EntityType);

        string find = $"""
            SELECT id AS "Id", false AS "Created", {SameCommand("@script", "@parameters::jsonb")} AS "Same"
            FROM {commands}
            WHERE base_key = @base_key AND caller_key = @caller_key AND {HoldingTheirKey(context)}
            """;

        NpgsqlParameter[] findParameters =
        [
            new NpgsqlParameter<string>("base_key", baseKey),
            new NpgsqlParameter<string>("caller_key", callerKey),
            new NpgsqlParameter<int>("mode", (int)mode),
            new NpgsqlParameter<string>("script", script),
            new NpgsqlParameter<string>("parameters", parameters),
        ];

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        // Rule 7: a command that no poll took ends at its deadline, by the node's clock, which frees its key. Of a take
        // and this change, the first to commit wins: this statement waits for a take that holds the row, and then finds
        // the command HandedOut.
        await context.Commands
            .Where(command => command.BaseKey == baseKey
                && command.CallerKey == callerKey
                && command.Phase == CommandPhaseEnum.Waiting
                && command.OutcomeKind == null
                && command.Deadline <= now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(command => command.OutcomeKind, OutpostOutcomeKindEnum.Expired)
                    .SetProperty(command => command.EndedAt, now),
                cancellationToken);

        List<CommandMatch> found = await context.Database
            .SqlQueryRaw<CommandMatch>(find, findParameters)
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return found.SingleOrDefault();
    }

    // The holder of a base key, in one statement; null when no agent holds it. Declared and WriteOpen are those of the
    // holder's newest poll, and whether the holder is connected is judged as for ListAgentsAsync (IsConnected).
    public async Task<BaseHolder?> FindBaseAsync(string baseKey, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var holder = await (
            from claim in context.BaseClaims
            join agent in context.Agents on claim.AgentId equals agent.Id
            join agentBase in context.AgentBases
                on new { claim.AgentId, claim.Key } equals new { agentBase.AgentId, agentBase.Key } into declared
            from agentBase in declared.DefaultIfEmpty()
            where claim.Key == baseKey
            select new
            {
                claim.AgentId,
                Declared = agentBase != null,
                WriteOpen = agentBase != null && agentBase.WriteOpen,
                agent.State,
                agent.LeaseEndsAt,
            })
            .SingleOrDefaultAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        if (holder is null)
        {
            return null;
        }

        bool connected = IsConnected(holder.State, holder.LeaseEndsAt, now);

        return new BaseHolder(holder.AgentId, holder.Declared, holder.WriteOpen, connected);
    }

    // Creates the command, or finds the one that holds its base and caller key, in one short transaction under the lock
    // FOR SHARE of its agent row while the agent is Paired and connected; null otherwise (rules 4, 6, 13).
    //
    // The lock conflicts with the update of a revocation, so a revocation either ends a command created before it or
    // makes the create find the agent Revoked. A create that finds the key held by a command that has not committed yet
    // waits for it, then finds it, or creates its own when that one rolled back; the update of a command found changes
    // nothing and locks its row, so that RETURNING gives it as committed. Created tells the command given from the one
    // found by its identity; a created command is the same, so its bytes are not read again.
    public async Task<CommandMatch?> CreateCommandAsync(CommandRow command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        string agents = TableName(context, context.Agents.EntityType);
        string commands = TableName(context, context.Commands.EntityType);

        string lockAgent = $"""
            SELECT id AS "Value" FROM {agents}
            WHERE id = @agent_id AND state = @paired AND lease_ends_at > @now
            FOR SHARE
            """;

        string sameAsGiven = SameCommand(
            "convert_from(@bytes, 'UTF8')::jsonb ->> 'script'",
            "convert_from(@bytes, 'UTF8')::jsonb -> 'parameters'");

        string create = $"""
            INSERT INTO {commands} AS command
                (id, agent_id, base_key, mode, caller_key, bytes, signature, deadline, skew, outcome_limit, phase)
            VALUES
                (@id, @agent_id, @base_key, @mode, @caller_key, @bytes, @signature, @deadline, @skew, @outcome_limit,
                 @waiting)
            ON CONFLICT (base_key, caller_key) WHERE {HoldingTheirKey(context)}
            DO UPDATE SET phase = command.phase
            RETURNING
                command.id AS "Id",
                command.id = @id AS "Created",
                CASE WHEN command.id = @id THEN true ELSE {sameAsGiven} END AS "Same"
            """;

        NpgsqlParameter[] lockParameters =
        [
            new NpgsqlParameter<Guid>("agent_id", command.AgentId),
            new NpgsqlParameter<int>("paired", (int)OutpostAgentStateEnum.Paired),
            new NpgsqlParameter<DateTimeOffset>("now", now),
        ];

        NpgsqlParameter[] createParameters =
        [
            new NpgsqlParameter<Guid>("id", command.Id),
            new NpgsqlParameter<Guid>("agent_id", command.AgentId),
            new NpgsqlParameter<string>("base_key", command.BaseKey),
            new NpgsqlParameter<int>("mode", (int)command.Mode),
            new NpgsqlParameter<string?>("caller_key", command.CallerKey),
            new NpgsqlParameter<byte[]>("bytes", command.Bytes),
            new NpgsqlParameter<byte[]>("signature", command.Signature),
            new NpgsqlParameter<DateTimeOffset>("deadline", command.Deadline),
            new NpgsqlParameter<TimeSpan>("skew", command.Skew),
            new NpgsqlParameter<int>("outcome_limit", command.OutcomeLimit),
            new NpgsqlParameter<int>("waiting", (int)CommandPhaseEnum.Waiting),
        ];

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        List<Guid> locked = await context.Database
            .SqlQueryRaw<Guid>(lockAgent, lockParameters)
            .ToListAsync(cancellationToken);

        if (locked.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        List<CommandMatch> match = await context.Database
            .SqlQueryRaw<CommandMatch>(create, createParameters)
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return match.Single();
    }

    // Ends the command when time made its end due, then reads it, in one short transaction (rules 7, 11): Expired when
    // no poll took it by its deadline, by the node's clock; once taken, at or after the end of its agent's lease plus
    // its run limit, Unknown `disconnected` for a Write and Failed `lost` for a Read. Outcome is the command's outcome,
    // and Due the moment time can next end it, as DueAt gives it.
    //
    // A waiting call is the reader that makes the change time made due, so nothing sweeps. The change waits for a
    // take or an outcome that holds the row and then finds the command as they left it: the first to commit wins.
    public async Task<CommandPass> PassCommandAsync(Guid commandId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        string agents = TableName(context, context.Agents.EntityType);
        string commands = TableName(context, context.Commands.EntityType);

        string end = $"""
            UPDATE {commands} AS command
            SET outcome_kind = CASE
                    WHEN command.phase = @waiting THEN @expired
                    WHEN command.mode = @write THEN @unknown
                    ELSE @failed
                END,
                outcome_code = CASE
                    WHEN command.phase = @waiting THEN NULL
                    WHEN command.mode = @write THEN @disconnected
                    ELSE @lost
                END,
                ended_at = @now
            FROM {agents} AS agent
            WHERE command.id = @command_id
                AND agent.id = command.agent_id
                AND command.outcome_kind IS NULL
                AND {DueAt} <= @now
            """;

        NpgsqlParameter[] endParameters =
        [
            new NpgsqlParameter<Guid>("command_id", commandId),
            new NpgsqlParameter<DateTimeOffset>("now", now),
            new NpgsqlParameter<int>("waiting", (int)CommandPhaseEnum.Waiting),
            new NpgsqlParameter<int>("write", (int)OutpostModeEnum.Write),
            new NpgsqlParameter<int>("expired", (int)OutpostOutcomeKindEnum.Expired),
            new NpgsqlParameter<int>("unknown", (int)OutpostOutcomeKindEnum.Unknown),
            new NpgsqlParameter<int>("failed", (int)OutpostOutcomeKindEnum.Failed),
            new NpgsqlParameter<string>("disconnected", OutpostOutcomeCodes.Disconnected),
            new NpgsqlParameter<string>("lost", OutpostOutcomeCodes.Lost),
        ];

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        await context.Database.ExecuteSqlRawAsync(end, endParameters, cancellationToken);

        var row = await (
            from command in context.Commands
            join agent in context.Agents on command.AgentId equals agent.Id
            where command.Id == commandId
            select new
            {
                command.Mode,
                command.Phase,
                command.Deadline,
                command.RunLimit,
                command.OutcomeKind,
                command.OutcomeCode,
                command.Outcome,
                agent.LeaseEndsAt,
            })
            .SingleAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        // As DueAt: a command that a poll took has a run limit, and its agent a lease.
        DateTimeOffset due = row.Phase == CommandPhaseEnum.Waiting
            ? row.Deadline
            : row.LeaseEndsAt!.Value + row.RunLimit!.Value;

        OutpostOutcome? outcome = ToOutcome(commandId, row.Mode, row.OutcomeKind, row.OutcomeCode, row.Outcome);

        return new CommandPass(outcome, due);
    }

    // The mode and the outcome limit of a command; null when there is none of that identity.
    public async Task<CommandLimits?> FindCommandLimitsAsync(Guid commandId, CancellationToken cancellationToken)
    {
        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        CommandLimits? limits = await context.Commands
            .Where(command => command.Id == commandId)
            .Select(command => new CommandLimits(command.Mode, command.OutcomeLimit))
            .SingleOrDefaultAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return limits;
    }

    // Keeps the outcome of the agent, in one statement, when the command is the agent's, not Waiting, and has no outcome
    // or a replaceable one; true when it changed the row (rule 11). The outcome is kept as its bytes came (§8), and its
    // retention counts from now.
    //
    // Of two outcomes of one command, the second waits for the row and then finds the first kept, which is not
    // replaceable: it changes nothing.
    public async Task<bool> KeepOutcomeAsync(
        Guid agentId,
        Guid commandId,
        OutpostOutcomeMessage message,
        ReadOnlyMemory<byte> outcome,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        OutpostOutcomeKindEnum kind = message.Kind switch
        {
            OutpostAgentOutcomeKindEnum.Succeeded => OutpostOutcomeKindEnum.Succeeded,
            OutpostAgentOutcomeKindEnum.Failed => OutpostOutcomeKindEnum.Failed,
            OutpostAgentOutcomeKindEnum.Refused => OutpostOutcomeKindEnum.Refused,
            OutpostAgentOutcomeKindEnum.Unknown => OutpostOutcomeKindEnum.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(message)),
        };

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        string commands = TableName(context, context.Commands.EntityType);

        string keep = $"""
            UPDATE {commands}
            SET outcome_kind = @kind, outcome_code = @code, outcome = @outcome, ended_at = @now
            WHERE id = @command_id
                AND agent_id = @agent_id
                AND phase <> @waiting
                AND (outcome_kind IS NULL OR {Replaceable})
            """;

        NpgsqlParameter[] parameters =
        [
            new NpgsqlParameter<Guid>("command_id", commandId),
            new NpgsqlParameter<Guid>("agent_id", agentId),
            new NpgsqlParameter<int>("kind", (int)kind),
            new NpgsqlParameter<string?>("code", message.Code),
            new NpgsqlParameter<ReadOnlyMemory<byte>>("outcome", outcome),
            new NpgsqlParameter<DateTimeOffset>("now", now),
            new NpgsqlParameter<int>("waiting", (int)CommandPhaseEnum.Waiting),
            new NpgsqlParameter<int>("unknown", (int)OutpostOutcomeKindEnum.Unknown),
            new NpgsqlParameter<int>("failed", (int)OutpostOutcomeKindEnum.Failed),
        ];

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        int kept = await context.Database.ExecuteSqlRawAsync(keep, parameters, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return kept == 1;
    }

    // Deletes the commands that ended a retention ago or more, and those with no outcome whose end by time was due that
    // long ago, in statements of at most 1 000 rows that skip locked rows; gives the number deleted.
    //
    // A command with no outcome is removed as if it had been ended when due and removed a retention later, without an
    // update. Each statement is a transaction of its own, so the purges of several nodes share the work and none waits
    // for another; a row that a call or a poll holds is left for the next round.
    public async Task<int> PurgeCommandsAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = _timeProvider.GetUtcNow() - retention;

        await using StoreContext context = StoreContextFactory.Create(_connectionString, _schema);

        string agents = TableName(context, context.Agents.EntityType);
        string commands = TableName(context, context.Commands.EntityType);

        string purge = $"""
            DELETE FROM {commands}
            WHERE id IN (
                SELECT command.id
                FROM {commands} AS command
                JOIN {agents} AS agent ON agent.id = command.agent_id
                WHERE command.ended_at <= @cutoff OR (command.outcome_kind IS NULL AND {DueAt} <= @cutoff)
                LIMIT @batch
                FOR UPDATE OF command SKIP LOCKED)
            """;

        int purged = 0;
        int deleted;

        do
        {
            NpgsqlParameter[] parameters =
            [
                new NpgsqlParameter<DateTimeOffset>("cutoff", cutoff),
                new NpgsqlParameter<int>("waiting", (int)CommandPhaseEnum.Waiting),
                new NpgsqlParameter<int>("batch", PurgeBatch),
            ];

            await using IDbContextTransaction transaction =
                await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            deleted = await context.Database.ExecuteSqlRawAsync(purge, parameters, cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            purged += deleted;
        }
        while (deleted == PurgeBatch);

        return purged;
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

    // The list of received commands of the newest poll, after its claims, in two statements (rules 7, 10, 11). Gives the
    // commands the second made Expired.
    //
    // The first makes the agent's HandedOut commands that the list holds Received: they reached the agent, so they are
    // never handed out again nor made Expired, which would free a key whose Write may still run. Every HandedOut
    // command of the agent that is left is one that the list does not hold. The second makes those Expired when their
    // outcome is none or replaceable and the poll's signed time is later than their deadline plus their skew: from then
    // on the agent refuses them by its own clock, so they never run. Identities of no command of this agent change
    // nothing. Each statement waits for a row that an outcome holds, and then finds that outcome: the first to commit
    // wins. The second finds its commands through the two partial indexes on the agent: that of the take, for no
    // outcome, and that of the expiry, for an outcome that is none or replaceable.
    private static async Task<IReadOnlyList<Guid>> ReconcileReceivedAsync(
        StoreContext context,
        Guid agentId,
        OutpostPollRequest poll,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string commands = TableName(context, context.Commands.EntityType);

        string expire = $"""
            UPDATE {commands}
            SET outcome_kind = @expired, outcome_code = NULL, outcome = NULL, ended_at = @now
            WHERE agent_id = @agent_id
                AND phase = @handed_out
                AND (outcome_kind IS NULL OR {Replaceable})
                AND deadline + skew < @time
            RETURNING id AS "Value"
            """;

        NpgsqlParameter[] parameters =
        [
            new NpgsqlParameter<Guid>("agent_id", agentId),
            new NpgsqlParameter<DateTimeOffset>("now", now),
            new NpgsqlParameter<DateTimeOffset>("time", poll.Time),
            new NpgsqlParameter<int>("handed_out", (int)CommandPhaseEnum.HandedOut),
            new NpgsqlParameter<int>("expired", (int)OutpostOutcomeKindEnum.Expired),
            new NpgsqlParameter<int>("unknown", (int)OutpostOutcomeKindEnum.Unknown),
            new NpgsqlParameter<int>("failed", (int)OutpostOutcomeKindEnum.Failed),
        ];

        Guid[] received = [.. poll.Received];

        await context.Commands
            .Where(command => command.AgentId == agentId
                && command.Phase == CommandPhaseEnum.HandedOut
                && received.Contains(command.Id))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(command => command.Phase, CommandPhaseEnum.Received),
                cancellationToken);

        return await context.Database.SqlQueryRaw<Guid>(expire, parameters).ToListAsync(cancellationToken);
    }

    // Rule 4: whether a row of commands is the same command as the one given, over the parameter @mode: its mode, its
    // script compared exactly and its parameters compared as JSON values, both read from its bytes, against the script
    // and the parameters given as SQL. A jsonb compares numbers by their value and objects whatever the order of their
    // members. The bytes are read as UTF-8 text, which a database in the encoding UTF8 holds whole.
    private static string SameCommand(string script, string parameters)
    {
        return $"""
            (mode = @mode
                AND convert_from(bytes, 'UTF8')::jsonb ->> 'script' = {script}
                AND convert_from(bytes, 'UTF8')::jsonb -> 'parameters' = {parameters})
            """;
    }

    // The filter of the unique index of base and caller key, as the model has it: the commands that hold their key,
    // every one but those that ended as Expired or Refused (rule 4).
    private static string HoldingTheirKey(StoreContext context)
    {
        return context.Commands.EntityType.GetIndexes().Single(index => index.IsUnique).GetFilter()!;
    }

    // An agent is connected while it is Paired and its lease ends later than the store's now. The lock of
    // CreateCommandAsync states the same condition in its SQL.
    private static bool IsConnected(OutpostAgentStateEnum state, DateTimeOffset? leaseEndsAt, DateTimeOffset now)
    {
        return state == OutpostAgentStateEnum.Paired && leaseEndsAt > now;
    }

    // The outcome of a command as a call returns it: null while it has none; the agent's, read from its object for the
    // mode of the command (§8); or one the server set, which is its kind and its code alone.
    private static OutpostOutcome? ToOutcome(
        Guid commandId,
        OutpostModeEnum mode,
        OutpostOutcomeKindEnum? kind,
        string? code,
        byte[]? outcome)
    {
        if (kind is not OutpostOutcomeKindEnum set)
        {
            return null;
        }

        if (outcome is not null)
        {
            OutpostOutcomeMessage message = OutpostCodec.ReadOutcome(mode, outcome);

            return message.Kind switch
            {
                OutpostAgentOutcomeKindEnum.Succeeded => OutpostOutcome.Succeeded(
                    commandId,
                    message.Result!.Value,
                    message.Messages,
                    message.MessagesTrimmed),
                OutpostAgentOutcomeKindEnum.Failed => OutpostOutcome.Failed(
                    commandId,
                    message.Code!,
                    message.ErrorText,
                    message.Messages,
                    message.MessagesTrimmed),
                OutpostAgentOutcomeKindEnum.Refused => OutpostOutcome.Refused(commandId, message.Code!),
                OutpostAgentOutcomeKindEnum.Unknown => OutpostOutcome.Unknown(commandId, message.Code!),
                _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
            };
        }

        return set switch
        {
            OutpostOutcomeKindEnum.Expired => OutpostOutcome.Expired(commandId),
            OutpostOutcomeKindEnum.Refused => OutpostOutcome.Refused(commandId, code!),
            OutpostOutcomeKindEnum.Unknown => OutpostOutcome.Unknown(commandId, code!),
            OutpostOutcomeKindEnum.Failed => OutpostOutcome.Failed(commandId, code!, null, [], false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    // The state for the answer to the poll of this number: Newest stays the recorded number when it is not this one, and
    // is null when it is.
    private static PollState ForPoll(PollState state, long number)
    {
        return state.Newest == number ? state with { Newest = null } : state;
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
