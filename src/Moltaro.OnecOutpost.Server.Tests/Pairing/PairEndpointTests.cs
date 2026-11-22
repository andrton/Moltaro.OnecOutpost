using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using Moltaro.OnecOutpost.TestSupport.Store;

namespace Moltaro.OnecOutpost.Server.Tests.Pairing;

// Pairing over loopback HTTP, with test agents (concept, "Pairing"; §6.1 of docs/protocol.md): a token pairs its agent
// once, before its expiry, with an answer signed by the server's key that repeats the key the server registered, and
// the secret is gone with the token. Every failure gets the same bare 403 without a body, whatever failed, and changes
// nothing. The fake clock starts at midnight of 1 January 2000, UTC.
[Trait("Requires", "PostgreSQL")]
public sealed class PairEndpointTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // The options of the answer are not the defaults, so that the answer shows the options of the node.
    [Fact]
    public async Task A_token_pairs_its_agent_with_a_signed_answer_that_repeats_the_registered_key_and_its_secret_is_gone()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "pair_succeeds",
            options =>
            {
                options.PollTimeout = TimeSpan.FromSeconds(20);
                options.LeaseGrace = TimeSpan.FromSeconds(15);
            },
            cancellationToken);
        using TestAgent agent = await server.CreateAgentAsync(cancellationToken);
        server.Clock.Advance(TimeSpan.FromMinutes(1));
        DateTimeOffset pairedAt = server.Clock.GetUtcNow();

        // Pin checks the answer as the agent does: the fingerprint of its server key is the token's, its signature
        // verifies with that key for this request, and it names this agent and the key it sent.
        OutpostPairAnswer answer = await agent.PairAsync(cancellationToken);

        Assert.Equal(agent.PublicKey, answer.Key);
        Assert.Equal(pairedAt, answer.Time);
        Assert.Equal(TimeSpan.FromSeconds(20), answer.PollTimeout);
        Assert.Equal(TimeSpan.FromSeconds(15), answer.LeaseGrace);

        AgentRow? row = await server.Schema.FindAgentAsync(agent.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(OutpostAgentStateEnum.Paired, row.State);
        Assert.Equal(agent.PublicKey, row.PublicKey);
        Assert.Equal(pairedAt, row.PairedAt);
        Assert.Null(await server.Schema.FindTokenAsync(agent.Id, cancellationToken));
    }

    // Each failure against a token that is otherwise good, one at a time: the store holds after the request what it
    // held before it, so the agent of the token is still pending with its token, or, after a second use, paired with
    // the key of the first.
    [Theory]
    [InlineData(PairFailureEnum.SecondUse)]
    [InlineData(PairFailureEnum.ExpiredToken)]
    [InlineData(PairFailureEnum.WrongProof)]
    [InlineData(PairFailureEnum.SwappedKey)]
    [InlineData(PairFailureEnum.WrongSignature)]
    [InlineData(PairFailureEnum.BrokenBody)]
    public async Task Every_failure_of_pair_gets_the_same_bare_403_and_changes_nothing(PairFailureEnum failure)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"pair_fails_{failure.ToString().ToLowerInvariant()}",
            cancellationToken);
        OutpostPairingToken token = await server.Server.CreatePairingTokenAsync("Test agent", cancellationToken);
        using TestAgent agent = new(server.Client, server.Clock, token);

        // Another agent with the same token, and so the same identity, but a key of its own.
        using TestAgent other = new(server.Client, server.Clock, token);

        if (failure == PairFailureEnum.SecondUse)
        {
            await agent.PairAsync(cancellationToken);
        }

        if (failure == PairFailureEnum.ExpiredToken)
        {
            server.Clock.Advance(token.ExpiresAt - server.Clock.GetUtcNow());
        }

        byte[] anotherSecret = RandomNumberGenerator.GetBytes(16);
        byte[] proofOfAnotherSecret = OutpostPairingProof.Compute(anotherSecret, agent.Id, agent.PublicKey);
        byte[] memberTwice = [.. "{\"agentVersion\":\"0.1.0\","u8, .. agent.PairBody().AsSpan(1)];
        byte[] signedByOther = other.Sign(OutpostRequestKindEnum.Pair, agent.PairBody());

        (byte[] body, byte[] signature) = failure switch
        {
            PairFailureEnum.SecondUse => Signed(other, other.PairBody()),
            PairFailureEnum.ExpiredToken => Signed(agent, agent.PairBody()),
            PairFailureEnum.WrongProof => Signed(agent, PairBody(agent.PublicKey, proofOfAnotherSecret)),
            PairFailureEnum.SwappedKey => Signed(other, PairBody(other.PublicKey, agent.Proof)),
            PairFailureEnum.WrongSignature => (agent.PairBody(), signedByOther),
            PairFailureEnum.BrokenBody => Signed(agent, memberTwice),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        AgentAnswer answer = await agent.SendAsync(OutpostRequestKindEnum.Pair, body, signature, cancellationToken);

        Assert.Equal(403, answer.Status);
        Assert.True(answer.IsBare);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // The clock stands between two microseconds when the token is made, so that an ExpiresAt that is not what the
    // store keeps would be later than the store's expiry, and the token would stop working before it. One tick before
    // ExpiresAt, the node's time reaches the store as the microsecond before it: Npgsql truncates a time to the
    // microsecond of a timestamptz.
    [Fact]
    public async Task A_token_pairs_until_the_moment_of_its_ExpiresAt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "pair_before_expiry",
            cancellationToken);
        server.Clock.Advance(TimeSpan.FromTicks(2));
        OutpostPairingToken token = await server.Server.CreatePairingTokenAsync("Test agent", cancellationToken);
        using TestAgent agent = new(server.Client, server.Clock, token);

        server.Clock.Advance(token.ExpiresAt - server.Clock.GetUtcNow() - TimeSpan.FromTicks(1));

        await agent.PairAsync(cancellationToken);
    }

    // The one race of pairing, driven to a known order as the race of the store's redemptions is: another connection
    // holds the row of the token locked until both pairings are seen waiting for it, and then lets it go unchanged.
    // Which pairing wins is not the subject; that one is paired with a verified answer, and the other gets the bare
    // 403, is.
    [Fact]
    public async Task Of_two_pairings_racing_on_one_token_one_wins_and_the_other_gets_the_bare_403()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(postgres, "pair_raced", cancellationToken);
        OutpostPairingToken token = await server.Server.CreatePairingTokenAsync("Test agent", cancellationToken);
        using TestAgent first = new(server.Client, server.Clock, token);
        using TestAgent second = new(server.Client, server.Clock, token);

        await using HeldLock tokenLock = await HeldLock.TakeAsync(
            server.Schema.ConnectionString,
            $"SELECT agent_id FROM {server.Schema.Name}.pairing_tokens WHERE agent_id = '{token.AgentId}' FOR UPDATE",
            cancellationToken);

        Task<AgentAnswer> firstPairing = first.SendPairAsync(cancellationToken);
        Task<AgentAnswer> secondPairing = second.SendPairAsync(cancellationToken);

        await tokenLock.WaitForAsync([firstPairing, secondPairing], cancellationToken);
        await tokenLock.ReleaseAsync(cancellationToken);

        AgentAnswer firstAnswer = await firstPairing;
        AgentAnswer secondAnswer = await secondPairing;

        bool firstWon = firstAnswer.Status == 200;
        TestAgent winner = firstWon ? first : second;
        AgentAnswer lost = firstWon ? secondAnswer : firstAnswer;

        winner.Pin(firstWon ? firstAnswer : secondAnswer);
        Assert.Equal(403, lost.Status);
        Assert.True(lost.IsBare);

        AgentRow? row = await server.Schema.FindAgentAsync(token.AgentId, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(winner.PublicKey, row.PublicKey);
    }

    private static byte[] PairBody(byte[] key, byte[] proof)
    {
        return OutpostCodec.Write(new OutpostPairRequest(PollRequests.AgentVersion, key, proof));
    }

    private static (byte[] Body, byte[] Signature) Signed(TestAgent signer, byte[] body)
    {
        return (body, signer.Sign(OutpostRequestKindEnum.Pair, body));
    }
}
