using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.Server.Tests.Migrations;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// The polls of the store against a real PostgreSQL, with the fake clock: what a poll that becomes the newest records,
// that an older poll changes nothing (concept rule 10), the lease of §6.2 of docs/protocol.md, and what the answer to
// a poll reads when its hold ends. The hold, the grace and the skew are given as the endpoint of poll gives them, with
// the defaults of the options (StoreAgent). Each test works in a schema of its own. The fake clock starts at midnight
// of 1 January 2000, UTC, and moves by whole seconds, so a time reads back as it was written.
[Trait("Requires", "PostgreSQL")]
public sealed class PollTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const int ClockSkewSeconds = 120;

    [Fact]
    public async Task FindAgentAsync_gives_the_row_of_an_agent_and_null_for_an_identity_the_store_does_not_know()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "agent_found", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);

        AgentRow? found = await store.FindAgentAsync(agent.Id, cancellationToken);

        Assert.NotNull(found);
        Assert.Equivalent(await schema.FindAgentAsync(agent.Id, cancellationToken), found, strict: true);
        Assert.Null(await store.FindAgentAsync(Guid.NewGuid(), cancellationToken));
    }

    [Fact]
    public async Task A_poll_that_becomes_the_newest_records_its_number_the_node_time_its_version_and_its_bases()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "poll_recorded", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        DateTimeOffset recordedAt = clock.GetUtcNow();
        OutpostPollRequest poll = Create(
            5,
            recordedAt,
            StoreAgent.PollTimeout,
            [ReadAndWrite("accounting"), ReadOnly("warehouse")]);

        PollState state = await RecordAsync(store, agent.Id, poll, cancellationToken);

        Assert.False(state.Revoked);
        Assert.Null(state.Newest);
        Assert.Equal<string>(["accounting", "warehouse"], state.Held);
        Assert.Empty(state.Claimed);

        AgentRow? row = await schema.FindAgentAsync(agent.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(5, row.NewestPoll);
        Assert.Equal(recordedAt, row.LastPollAt);
        Assert.Equal(AgentVersion, row.AgentVersion);
        Assert.Equal<(string, bool)>(
            [("accounting", true), ("warehouse", false)],
            await schema.FindBasesAsync(agent.Id, cancellationToken));
    }

    // The lease ends at the earlier of two moments: the recording plus the hold and the grace, and the signed time plus
    // the skew, the hold and the grace (§6.2). A poll on time grants the first; one whose time is behind the node's by
    // more than the skew grants less, by the part of its lag beyond the skew.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(ClockSkewSeconds + 7, 7)]
    public async Task The_lease_ends_at_the_hold_plus_the_grace_after_the_recording_less_the_part_of_the_lag_beyond_the_skew(
        int lagSeconds,
        int lessSeconds)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(
            postgres,
            $"lease_with_lag_{lagSeconds}",
            cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        DateTimeOffset recordedAt = clock.GetUtcNow();
        DateTimeOffset signedAt = recordedAt - TimeSpan.FromSeconds(lagSeconds);
        OutpostPollRequest poll = Create(1, signedAt, StoreAgent.PollTimeout, [ReadOnly("accounting")]);

        await store.RecordPollAsync(
            agent.Id,
            poll,
            StoreAgent.Hold,
            StoreAgent.LeaseGrace,
            TimeSpan.FromSeconds(ClockSkewSeconds),
            cancellationToken);

        TimeSpan lease = StoreAgent.Hold + StoreAgent.LeaseGrace - TimeSpan.FromSeconds(lessSeconds);

        AgentRow? row = await schema.FindAgentAsync(agent.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(recordedAt + lease, row.LeaseEndsAt);
    }

    // The older poll comes a minute later and declares another base, with another version: a poll that counted as newer
    // would record its time, its lease and its version, replace the bases and claim the other key.
    [Fact]
    public async Task An_older_poll_changes_nothing_and_gets_the_newest_number_with_the_lists_of_the_newest_poll()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "poll_older", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        OutpostPollRequest newest = Create(10, clock.GetUtcNow(), StoreAgent.PollTimeout, [ReadOnly("accounting")]);
        await RecordAsync(store, agent.Id, newest, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        SchemaSnapshot before = await schema.TakeSnapshotAsync(cancellationToken);
        OutpostPollRequest older = Create(9, clock.GetUtcNow(), StoreAgent.PollTimeout, [ReadOnly("warehouse")]) with
        {
            AgentVersion = "0.2.0",
        };

        PollState state = await RecordAsync(store, agent.Id, older, cancellationToken);

        Assert.False(state.Revoked);
        Assert.Equal(10, state.Newest);
        Assert.Equal<string>(["accounting"], state.Held);
        Assert.Empty(state.Claimed);
        Assert.Equal(before.Lines, (await schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // The replay comes a minute after the poll: a store that counted it as newer would record its time and its lease
    // again.
    [Fact]
    public async Task An_exact_replay_of_the_newest_poll_changes_nothing_and_gets_the_number_of_that_poll()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "poll_replayed", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        OutpostPollRequest newest = Create(10, clock.GetUtcNow(), StoreAgent.PollTimeout, [ReadOnly("accounting")]);
        await RecordAsync(store, agent.Id, newest, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        SchemaSnapshot before = await schema.TakeSnapshotAsync(cancellationToken);

        PollState state = await RecordAsync(store, agent.Id, newest, cancellationToken);

        Assert.False(state.Revoked);
        Assert.Equal(10, state.Newest);
        Assert.Equal<string>(["accounting"], state.Held);
        Assert.Empty(state.Claimed);
        Assert.Equal(before.Lines, (await schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    [Fact]
    public async Task A_newer_poll_replaces_the_bases_of_its_agent_whole()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "poll_replaces_bases", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.PollAsync([ReadAndWrite("accounting"), ReadOnly("warehouse")], cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));

        PollState state = await agent.PollAsync([ReadOnly("accounting"), ReadAndWrite("sales")], cancellationToken);

        Assert.Null(state.Newest);
        Assert.Equal<(string, bool)>(
            [("accounting", false), ("sales", true)],
            await schema.FindBasesAsync(agent.Id, cancellationToken));

        AgentRow? row = await schema.FindAgentAsync(agent.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(agent.Number, row.NewestPoll);
        Assert.Equal(clock.GetUtcNow(), row.LastPollAt);
    }

    // Concept rule 13: the polls of an agent that is not paired take nothing. The poll declares a key that no agent
    // holds, which a poll that was recorded would claim.
    [Theory]
    [InlineData(OutpostAgentStateEnum.Pending)]
    [InlineData(OutpostAgentStateEnum.Revoked)]
    public async Task A_poll_of_an_agent_that_is_not_paired_changes_nothing_and_says_revoked(OutpostAgentStateEnum agentState)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(
            postgres,
            $"poll_of_{agentState.ToString().ToLowerInvariant()}",
            cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid agentId = Guid.NewGuid();
        DateTimeOffset expiresAt = clock.GetUtcNow() + TimeSpan.FromMinutes(15);
        await store.CreatePairingTokenAsync(agentId, "Test agent", new byte[16], expiresAt, cancellationToken);

        if (agentState == OutpostAgentStateEnum.Revoked)
        {
            OutpostPollRequest first = Create(1, clock.GetUtcNow(), StoreAgent.PollTimeout, [ReadOnly("accounting")]);

            Assert.True(await store.RedeemPairingTokenAsync(agentId, new byte[91], cancellationToken));
            await RecordAsync(store, agentId, first, cancellationToken);
            Assert.True(await store.RevokeAgentAsync(agentId, cancellationToken));
        }

        clock.Advance(TimeSpan.FromMinutes(1));
        SchemaSnapshot before = await schema.TakeSnapshotAsync(cancellationToken);
        OutpostPollRequest poll = Create(2, clock.GetUtcNow(), StoreAgent.PollTimeout, [ReadOnly("sales")]);

        PollState state = await RecordAsync(store, agentId, poll, cancellationToken);

        Assert.True(state.Revoked);
        Assert.Equal(before.Lines, (await schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // Another agent holds one of the keys, which is claimed for this one.
    [Fact]
    public async Task The_state_of_the_newest_poll_has_no_newest_number_and_the_lists_of_the_keys_that_poll_declared()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "state_of_newest", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent holder = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await holder.PollAsync([ReadOnly("warehouse")], cancellationToken);
        await agent.PollAsync([ReadOnly("accounting"), ReadOnly("warehouse")], cancellationToken);
        clock.Advance(StoreAgent.Hold);

        PollState state = await store.ReadPollStateAsync(agent.Id, agent.Number, cancellationToken);

        Assert.False(state.Revoked);
        Assert.Null(state.Newest);
        Assert.Equal<string>(["accounting"], state.Held);
        Assert.Equal<string>(["warehouse"], state.Claimed);
    }

    // The newer poll no longer declares the key of the first, which the agent still holds: it is in neither list.
    [Fact]
    public async Task The_state_of_a_poll_that_a_newer_one_replaced_names_the_newest_number_with_the_lists_of_the_newest()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "state_of_replaced", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.PollAsync([ReadOnly("accounting")], cancellationToken);
        long replaced = agent.Number;
        await agent.PollAsync([ReadOnly("warehouse")], cancellationToken);

        PollState state = await store.ReadPollStateAsync(agent.Id, replaced, cancellationToken);

        Assert.False(state.Revoked);
        Assert.Equal(agent.Number, state.Newest);
        Assert.Equal<string>(["warehouse"], state.Held);
        Assert.Empty(state.Claimed);
    }

    [Fact]
    public async Task The_state_of_a_poll_whose_agent_was_revoked_since_says_revoked()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "state_of_revoked", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.PollAsync([ReadOnly("accounting")], cancellationToken);
        Assert.True(await store.RevokeAgentAsync(agent.Id, cancellationToken));

        PollState state = await store.ReadPollStateAsync(agent.Id, agent.Number, cancellationToken);

        Assert.True(state.Revoked);
    }

    private static Task<PollState> RecordAsync(
        OutpostStore store,
        Guid agentId,
        OutpostPollRequest poll,
        CancellationToken cancellationToken)
    {
        return store.RecordPollAsync(
            agentId,
            poll,
            StoreAgent.Hold,
            StoreAgent.LeaseGrace,
            StoreAgent.ClockSkew,
            cancellationToken);
    }
}
