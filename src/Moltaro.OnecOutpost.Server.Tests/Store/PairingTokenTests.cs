using System.Security.Cryptography;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Store;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// The pairing tokens of the store against a real PostgreSQL, with the fake clock. A token is the identity of its
// pending agent; it is redeemed once, before its expiry, and its secret is gone with it (concept, "Pairing"; the brief
// of S3). Each test works in a schema of its own. The fake clock starts at midnight of 1 January 2000, UTC, and moves by
// whole microseconds, the precision of a timestamptz, so a time reads back as it was written; a time taken from the
// clock of the database instead, years later, would show (PQ3).
[Trait("Requires", "PostgreSQL")]
public sealed class PairingTokenTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string AgentName = "Test agent";

    // The default lifetime of a token (concept, "Pairing"). The store keeps whatever expiry it is given.
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan OneMicrosecond = TimeSpan.FromMicroseconds(1);

    [Fact]
    public async Task A_created_token_has_a_pending_agent_and_its_secret_is_found()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "token_created", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid agentId = Guid.NewGuid();
        byte[] secret = NewSecret();
        DateTimeOffset createdAt = clock.GetUtcNow();

        await store.CreatePairingTokenAsync(agentId, AgentName, secret, createdAt + Lifetime, cancellationToken);

        AgentRow? agent = await schema.FindAgentAsync(agentId, cancellationToken);
        Assert.NotNull(agent);
        Assert.Equal(OutpostAgentStateEnum.Pending, agent.State);
        Assert.Equal(AgentName, agent.Name);
        Assert.Equal(createdAt, agent.CreatedAt);
        Assert.Equal(secret, await store.FindPairingSecretAsync(agentId, cancellationToken));
    }

    [Fact]
    public async Task The_secret_is_found_until_the_expiry()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "secret_before_expiry", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid agentId = Guid.NewGuid();
        byte[] secret = NewSecret();
        DateTimeOffset expiresAt = clock.GetUtcNow() + Lifetime;

        await store.CreatePairingTokenAsync(agentId, AgentName, secret, expiresAt, cancellationToken);
        clock.Advance(Lifetime - OneMicrosecond);

        Assert.Equal(secret, await store.FindPairingSecretAsync(agentId, cancellationToken));
    }

    [Fact]
    public async Task The_secret_is_not_found_from_the_expiry_on()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "secret_at_expiry", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid agentId = Guid.NewGuid();
        DateTimeOffset expiresAt = clock.GetUtcNow() + Lifetime;

        await store.CreatePairingTokenAsync(agentId, AgentName, NewSecret(), expiresAt, cancellationToken);
        clock.Advance(Lifetime);

        Assert.Null(await store.FindPairingSecretAsync(agentId, cancellationToken));
    }

    [Fact]
    public async Task A_redemption_makes_the_agent_paired_with_its_key_and_time_and_leaves_no_token()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "token_redeemed", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid agentId = Guid.NewGuid();
        byte[] key = NewPublicKey();
        DateTimeOffset expiresAt = clock.GetUtcNow() + Lifetime;

        await store.CreatePairingTokenAsync(agentId, AgentName, NewSecret(), expiresAt, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        DateTimeOffset pairedAt = clock.GetUtcNow();

        Assert.True(await store.RedeemPairingTokenAsync(agentId, key, cancellationToken));

        AgentRow? agent = await schema.FindAgentAsync(agentId, cancellationToken);
        Assert.NotNull(agent);
        Assert.Equal(OutpostAgentStateEnum.Paired, agent.State);
        Assert.Equal(key, agent.PublicKey);
        Assert.Equal(pairedAt, agent.PairedAt);
        Assert.Null(await schema.FindTokenAsync(agentId, cancellationToken));
    }

    [Fact]
    public async Task A_second_redemption_gives_false_and_leaves_the_key_and_time_of_the_first()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "token_redeemed_twice", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid agentId = Guid.NewGuid();
        byte[] firstKey = NewPublicKey();
        DateTimeOffset expiresAt = clock.GetUtcNow() + Lifetime;

        await store.CreatePairingTokenAsync(agentId, AgentName, NewSecret(), expiresAt, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        DateTimeOffset pairedAt = clock.GetUtcNow();
        Assert.True(await store.RedeemPairingTokenAsync(agentId, firstKey, cancellationToken));
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.False(await store.RedeemPairingTokenAsync(agentId, NewPublicKey(), cancellationToken));

        AgentRow? agent = await schema.FindAgentAsync(agentId, cancellationToken);
        Assert.NotNull(agent);
        Assert.Equal(firstKey, agent.PublicKey);
        Assert.Equal(pairedAt, agent.PairedAt);
    }

    // The token stays for the purge, which removes it with its agent.
    [Fact]
    public async Task A_redemption_from_the_expiry_on_gives_false_and_leaves_the_agent_pending_with_its_token()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "token_redeemed_at_expiry", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid agentId = Guid.NewGuid();
        DateTimeOffset expiresAt = clock.GetUtcNow() + Lifetime;

        await store.CreatePairingTokenAsync(agentId, AgentName, NewSecret(), expiresAt, cancellationToken);
        clock.Advance(Lifetime);

        Assert.False(await store.RedeemPairingTokenAsync(agentId, NewPublicKey(), cancellationToken));

        AgentRow? agent = await schema.FindAgentAsync(agentId, cancellationToken);
        Assert.NotNull(agent);
        Assert.Equal(OutpostAgentStateEnum.Pending, agent.State);
        Assert.NotNull(await schema.FindTokenAsync(agentId, cancellationToken));
    }

    // The store holds the token of another agent, which a redemption that ignored the identity would take.
    [Fact]
    public async Task A_redemption_of_an_identity_the_store_does_not_know_gives_false()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "unknown_identity_redeemed", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid knownId = Guid.NewGuid();
        DateTimeOffset expiresAt = clock.GetUtcNow() + Lifetime;

        await store.CreatePairingTokenAsync(knownId, AgentName, NewSecret(), expiresAt, cancellationToken);

        Assert.False(await store.RedeemPairingTokenAsync(Guid.NewGuid(), NewPublicKey(), cancellationToken));

        AgentRow? known = await schema.FindAgentAsync(knownId, cancellationToken);
        Assert.NotNull(known);
        Assert.Equal(OutpostAgentStateEnum.Pending, known.State);
    }

    // The store holds the token of another agent, whose secret a lookup that ignored the identity would give.
    [Fact]
    public async Task The_secret_of_an_identity_the_store_does_not_know_is_not_found()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "unknown_identity_found", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        DateTimeOffset expiresAt = clock.GetUtcNow() + Lifetime;

        await store.CreatePairingTokenAsync(Guid.NewGuid(), AgentName, NewSecret(), expiresAt, cancellationToken);

        Assert.Null(await store.FindPairingSecretAsync(Guid.NewGuid(), cancellationToken));
    }

    // The later token expires at the very time of the purge, and so has expired: a token is expired from its expiry on.
    [Fact]
    public async Task The_purge_removes_each_expired_token_with_its_pending_agent_and_gives_their_number()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "purge_of_expired", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid earlierId = Guid.NewGuid();
        Guid laterId = Guid.NewGuid();
        DateTimeOffset earlierExpiry = clock.GetUtcNow() + TimeSpan.FromMinutes(5);
        DateTimeOffset laterExpiry = clock.GetUtcNow() + TimeSpan.FromMinutes(10);

        await store.CreatePairingTokenAsync(earlierId, AgentName, NewSecret(), earlierExpiry, cancellationToken);
        await store.CreatePairingTokenAsync(laterId, AgentName, NewSecret(), laterExpiry, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(2, await store.PurgePairingTokensAsync(cancellationToken));

        Assert.Null(await schema.FindTokenAsync(earlierId, cancellationToken));
        Assert.Null(await schema.FindAgentAsync(earlierId, cancellationToken));
        Assert.Null(await schema.FindTokenAsync(laterId, cancellationToken));
        Assert.Null(await schema.FindAgentAsync(laterId, cancellationToken));
    }

    // The token expires one microsecond after the purge. Its secret, found afterwards, shows the token and its pending
    // agent as they were.
    [Fact]
    public async Task The_purge_leaves_a_token_that_has_not_expired()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "purge_of_unexpired", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid agentId = Guid.NewGuid();
        byte[] secret = NewSecret();
        DateTimeOffset expiresAt = clock.GetUtcNow() + Lifetime;

        await store.CreatePairingTokenAsync(agentId, AgentName, secret, expiresAt, cancellationToken);
        clock.Advance(Lifetime - OneMicrosecond);

        Assert.Equal(0, await store.PurgePairingTokensAsync(cancellationToken));

        Assert.Equal(secret, await store.FindPairingSecretAsync(agentId, cancellationToken));
    }

    // The purge runs long after the expiry that the agent's token had before its redemption.
    [Fact]
    public async Task The_purge_leaves_an_agent_that_is_paired()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "purge_of_paired", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid agentId = Guid.NewGuid();
        DateTimeOffset expiresAt = clock.GetUtcNow() + Lifetime;

        await store.CreatePairingTokenAsync(agentId, AgentName, NewSecret(), expiresAt, cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await store.RedeemPairingTokenAsync(agentId, NewPublicKey(), cancellationToken));
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, await store.PurgePairingTokensAsync(cancellationToken));

        AgentRow? agent = await schema.FindAgentAsync(agentId, cancellationToken);
        Assert.NotNull(agent);
        Assert.Equal(OutpostAgentStateEnum.Paired, agent.State);
    }

    // The one race of the sprint, driven to a known order: another connection holds the token's row locked until both
    // redemptions are seen waiting for it, and then lets it go unchanged. Which of the two takes the row first is not
    // the subject; that exactly one of them pairs the agent, with its own key, is.
    [Fact]
    public async Task Of_two_redemptions_waiting_for_one_token_only_one_gives_true_and_the_agent_has_its_key()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "token_redeemed_at_once", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid agentId = Guid.NewGuid();
        byte[] firstKey = NewPublicKey();
        byte[] secondKey = NewPublicKey();
        DateTimeOffset expiresAt = clock.GetUtcNow() + Lifetime;

        await store.CreatePairingTokenAsync(agentId, AgentName, NewSecret(), expiresAt, cancellationToken);

        await using HeldLock tokenLock = await HeldLock.TakeAsync(
            schema.ConnectionString,
            $"SELECT agent_id FROM {schema.Name}.pairing_tokens WHERE agent_id = '{agentId}' FOR UPDATE",
            cancellationToken);

        Task<bool> first = store.RedeemPairingTokenAsync(agentId, firstKey, cancellationToken);
        Task<bool> second = store.RedeemPairingTokenAsync(agentId, secondKey, cancellationToken);

        await tokenLock.WaitForAsync([first, second], cancellationToken);
        await tokenLock.ReleaseAsync(cancellationToken);

        bool firstWon = await first;
        bool secondWon = await second;

        Assert.NotEqual(firstWon, secondWon);

        AgentRow? agent = await schema.FindAgentAsync(agentId, cancellationToken);
        Assert.NotNull(agent);
        Assert.Equal(OutpostAgentStateEnum.Paired, agent.State);
        Assert.Equal(firstWon ? firstKey : secondKey, agent.PublicKey);
    }

    // The secret of a token: 16 random bytes (docs/protocol.md, §5).
    private static byte[] NewSecret()
    {
        return RandomNumberGenerator.GetBytes(16);
    }

    // Bytes in place of an agent's public key, as long as a P-256 key in SubjectPublicKeyInfo. The store keeps a key as
    // it is given; what the bytes encode is for the protocol to check.
    private static byte[] NewPublicKey()
    {
        return RandomNumberGenerator.GetBytes(91);
    }
}
