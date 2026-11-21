using System.Security.Cryptography;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// The list of agents of the store against a real PostgreSQL, with the fake clock: every agent by its creation and its
// identity, with the bases of its newest poll by key and whether it holds each (concept rule 14), and whether it is
// connected: Paired, with a lease that ends later than the store's now (rule 6, §6.2 of docs/protocol.md). Each test
// works in a schema of its own.
[Trait("Requires", "PostgreSQL")]
public sealed class AgentListTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // Two agents are created at one moment, the one with the greater identity first, and a third later with the least
    // identity, so that neither the order of creation alone nor the order of identities alone gives the list.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_list_gives_every_agent_by_its_creation_and_identity_with_its_name_state_and_times()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "agents_listed", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid least = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid paired = Guid.Parse("00000000-0000-0000-0000-000000000002");
        Guid pending = Guid.Parse("00000000-0000-0000-0000-000000000003");
        DateTimeOffset createdAt = clock.GetUtcNow();
        await CreateTokenAsync(store, pending, "Pending agent", clock, cancellationToken);
        await CreateTokenAsync(store, paired, "Paired agent", clock, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        DateTimeOffset pairedAt = clock.GetUtcNow();
        Assert.True(await store.RedeemPairingTokenAsync(paired, RandomNumberGenerator.GetBytes(91), cancellationToken));
        clock.Advance(TimeSpan.FromMinutes(1));
        DateTimeOffset revokedCreatedAt = clock.GetUtcNow();
        await CreateTokenAsync(store, least, "Revoked agent", clock, cancellationToken);
        Assert.True(await store.RevokeAgentAsync(least, cancellationToken));

        IReadOnlyList<OutpostAgent> agents = await store.ListAgentsAsync(cancellationToken);

        Assert.Equal<Guid>([paired, pending, least], agents.Select(agent => agent.Id));
        AssertNeverPolled(agents[0], "Paired agent", OutpostAgentStateEnum.Paired, createdAt, pairedAt);
        AssertNeverPolled(agents[1], "Pending agent", OutpostAgentStateEnum.Pending, createdAt, null);
        AssertNeverPolled(agents[2], "Revoked agent", OutpostAgentStateEnum.Revoked, revokedCreatedAt, null);
    }

    // Another agent holds one of the keys. Under the collation of the test database, en_US.utf8, "sales1" would sort
    // before "sales-2".
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_list_gives_the_bases_of_the_newest_poll_by_key_open_for_Read_with_their_Write_and_whether_the_agent_holds_each()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "agent_bases_listed", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent holder = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await holder.PollAsync([ReadOnly("sales1")], cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        DateTimeOffset polledAt = clock.GetUtcNow();
        await agent.PollAsync(
            [ReadOnly("warehouse"), ReadAndWrite("sales1"), ReadOnly("sales-2"), ReadAndWrite("accounting")],
            cancellationToken);

        IReadOnlyList<OutpostAgent> agents = await store.ListAgentsAsync(cancellationToken);

        OutpostAgent listed = agents.Single(candidate => candidate.Id == agent.Id);
        Assert.Equal(polledAt, listed.LastPollAt);
        Assert.Equal(AgentVersion, listed.AgentVersion);
        Assert.True(listed.Connected);
        Assert.Equal<OutpostBase>(
            [
                new OutpostBase("accounting", ReadOpen: true, WriteOpen: true, Held: true),
                new OutpostBase("sales-2", ReadOpen: true, WriteOpen: false, Held: true),
                new OutpostBase("sales1", ReadOpen: true, WriteOpen: true, Held: false),
                new OutpostBase("warehouse", ReadOpen: true, WriteOpen: false, Held: true),
            ],
            listed.Bases);
    }

    // The lease of a poll on time ends at its recording plus the hold and the grace (§6.2). A timestamptz keeps
    // microseconds, the step of the clock here.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task An_agent_is_connected_while_its_lease_runs_and_not_from_its_end_on()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "agent_connected", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.PollAsync([ReadOnly("accounting")], cancellationToken);

        clock.Advance(StoreAgent.Hold + StoreAgent.LeaseGrace - TimeSpan.FromMicroseconds(1));

        Assert.True(await IsConnectedAsync(store, agent.Id, cancellationToken));

        clock.Advance(TimeSpan.FromMicroseconds(1));

        Assert.False(await IsConnectedAsync(store, agent.Id, cancellationToken));
    }

    // The public contract: an agent that is not Paired is not connected, whatever its lease, so a revoked agent is
    // never shown as connected.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_revoked_agent_is_not_connected_whatever_its_lease_and_holds_none_of_its_bases()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "revoked_not_connected", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.PollAsync([ReadAndWrite("accounting")], cancellationToken);
        Assert.True(await store.RevokeAgentAsync(agent.Id, cancellationToken));

        IReadOnlyList<OutpostAgent> agents = await store.ListAgentsAsync(cancellationToken);

        OutpostAgent listed = Assert.Single(agents);
        Assert.Equal(OutpostAgentStateEnum.Revoked, listed.State);
        Assert.False(listed.Connected);
        Assert.Equal<OutpostBase>(
            [new OutpostBase("accounting", ReadOpen: true, WriteOpen: true, Held: false)],
            listed.Bases);
    }

    // An agent that the store knows from its token and maybe its pairing, without a poll.
    private static void AssertNeverPolled(
        OutpostAgent agent,
        string name,
        OutpostAgentStateEnum state,
        DateTimeOffset createdAt,
        DateTimeOffset? pairedAt)
    {
        Assert.Equal(name, agent.Name);
        Assert.Equal(state, agent.State);
        Assert.Equal(createdAt, agent.CreatedAt);
        Assert.Equal(pairedAt, agent.PairedAt);
        Assert.Null(agent.LastPollAt);
        Assert.False(agent.Connected);
        Assert.Null(agent.AgentVersion);
        Assert.Empty(agent.Bases);
    }

    private static Task CreateTokenAsync(
        OutpostStore store,
        Guid agentId,
        string agentName,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        byte[] secret = RandomNumberGenerator.GetBytes(16);
        DateTimeOffset expiresAt = clock.GetUtcNow() + TimeSpan.FromMinutes(15);

        return store.CreatePairingTokenAsync(agentId, agentName, secret, expiresAt, cancellationToken);
    }

    private static async Task<bool> IsConnectedAsync(
        OutpostStore store,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OutpostAgent> agents = await store.ListAgentsAsync(cancellationToken);

        return agents.Single(agent => agent.Id == agentId).Connected;
    }
}
