using System.Security.Cryptography;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.Server.Tests.Migrations;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// Revoking an agent in the store against a real PostgreSQL (concept rule 13): one short transaction deletes its token,
// makes it Revoked and frees the keys it held (rule 14). The bases of its newest poll stay, so that the list of agents
// still shows them, none held. Each test works in a schema of its own.
[Trait("Requires", "PostgreSQL")]
public sealed class RevocationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task Revoking_a_paired_agent_gives_true_and_frees_its_keys_for_the_next_agent_that_declares_them()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "revoke_paired", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent revoked = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent next = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await revoked.PollAsync([ReadAndWrite("accounting")], cancellationToken);

        Assert.True(await store.RevokeAgentAsync(revoked.Id, cancellationToken));

        AgentRow? row = await schema.FindAgentAsync(revoked.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(OutpostAgentStateEnum.Revoked, row.State);
        Assert.Null(await schema.FindHolderAsync("accounting", cancellationToken));
        Assert.Equal<(string, bool)>(
            [("accounting", true)],
            await schema.FindBasesAsync(revoked.Id, cancellationToken));

        PollState state = await next.PollAsync([ReadOnly("accounting")], cancellationToken);

        Assert.Equal<string>(["accounting"], state.Held);
    }

    [Fact]
    public async Task Revoking_a_revoked_agent_or_an_identity_the_store_does_not_know_gives_false_and_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "revoke_again", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.PollAsync([ReadOnly("accounting")], cancellationToken);
        Assert.True(await store.RevokeAgentAsync(agent.Id, cancellationToken));
        SchemaSnapshot before = await schema.TakeSnapshotAsync(cancellationToken);

        Assert.False(await store.RevokeAgentAsync(agent.Id, cancellationToken));
        Assert.False(await store.RevokeAgentAsync(Guid.NewGuid(), cancellationToken));

        Assert.Equal(before.Lines, (await schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // The public contract: revoking a pending agent ends its token, which then pairs nothing.
    [Fact]
    public async Task Revoking_a_pending_agent_ends_its_token()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "revoke_pending", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid agentId = Guid.NewGuid();
        byte[] secret = RandomNumberGenerator.GetBytes(16);
        DateTimeOffset expiresAt = clock.GetUtcNow() + TimeSpan.FromMinutes(15);
        await store.CreatePairingTokenAsync(agentId, "Test agent", secret, expiresAt, cancellationToken);

        Assert.True(await store.RevokeAgentAsync(agentId, cancellationToken));

        AgentRow? row = await schema.FindAgentAsync(agentId, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(OutpostAgentStateEnum.Revoked, row.State);
        Assert.Null(await schema.FindTokenAsync(agentId, cancellationToken));
        Assert.Null(await store.FindPairingSecretAsync(agentId, cancellationToken));
        Assert.False(await store.RedeemPairingTokenAsync(agentId, new byte[91], cancellationToken));
    }
}
