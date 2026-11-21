using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Tests.Loopback;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Bases;

// Base keys over loopback HTTP, through the polls of test agents and the public API (concept rule 14): a key belongs to
// the first agent that declares it until that agent is revoked or the application releases the key, a release is
// refused while the holder still declares the key, and every poll answer reports each key it declared as held or as
// claimed. The option PollTimeout is 1 second, and each poll is answered when its hold ends.
[Trait("Requires", "PostgreSQL")]
public sealed class BaseKeyTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_key_is_held_by_the_first_agent_that_declares_it_and_released_only_once_its_holder_no_longer_declares_it()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync("keys_released", cancellationToken);
        using TestAgent first = await server.PairAgentAsync(cancellationToken);
        using TestAgent second = await server.PairAgentAsync(cancellationToken);

        OutpostPollAnswer firstAnswer = await server.PollToTheEndOfItsHoldAsync(
            first,
            [ReadAndWrite("accounting"), ReadOnly("warehouse")],
            cancellationToken);
        AssertBases(firstAnswer, held: ["accounting", "warehouse"], claimed: []);

        OutpostPollAnswer secondAnswer = await server.PollToTheEndOfItsHoldAsync(
            second,
            [ReadOnly("accounting"), ReadOnly("sales")],
            cancellationToken);
        AssertBases(secondAnswer, held: ["sales"], claimed: ["accounting"]);

        Assert.Equal(OutpostReleaseResultEnum.StillDeclared, await ReleaseAsync(server, cancellationToken));

        firstAnswer = await server.PollToTheEndOfItsHoldAsync(first, [ReadOnly("warehouse")], cancellationToken);
        AssertBases(firstAnswer, held: ["warehouse"], claimed: []);

        Assert.Equal(OutpostReleaseResultEnum.Released, await ReleaseAsync(server, cancellationToken));
        Assert.Equal(OutpostReleaseResultEnum.NotHeld, await ReleaseAsync(server, cancellationToken));

        secondAnswer = await server.PollToTheEndOfItsHoldAsync(
            second,
            [ReadOnly("accounting"), ReadOnly("sales")],
            cancellationToken);
        AssertBases(secondAnswer, held: ["accounting", "sales"], claimed: []);

        IReadOnlyList<OutpostAgent> agents = await server.Server.GetAgentsAsync(cancellationToken);
        Assert.Equal<OutpostBase>(
            [new OutpostBase("warehouse", ReadOpen: true, WriteOpen: false, Held: true)],
            agents.Single(agent => agent.Id == first.Id).Bases);
        Assert.Equal<OutpostBase>(
            [
                new OutpostBase("accounting", ReadOpen: true, WriteOpen: false, Held: true),
                new OutpostBase("sales", ReadOpen: true, WriteOpen: false, Held: true),
            ],
            agents.Single(agent => agent.Id == second.Id).Bases);
    }

    // The public contract: a key freed while a poll that declares it is held is in bases.claimed of the answer to that
    // poll, since no agent holds it at that moment, and the next poll of the agent takes it.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_revocation_during_a_held_poll_frees_the_key_which_the_answer_reports_claimed_until_the_next_poll_holds_it()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync("keys_freed_by_revocation", cancellationToken);
        using TestAgent revoked = await server.PairAgentAsync(cancellationToken);
        using TestAgent next = await server.PairAgentAsync(cancellationToken);
        await server.PollToTheEndOfItsHoldAsync(revoked, [ReadOnly("accounting")], cancellationToken);
        TimeSpan hold = server.Options.PollTimeout;
        OutpostPollRequest poll = next.NextPoll(hold, [ReadOnly("accounting")]);
        DateTimeOffset start = server.Clock.GetUtcNow();

        Task<AgentAnswer> answer = next.PollAsync(poll, cancellationToken);
        await server.WaitForTimerAsync(start + hold, cancellationToken);
        Assert.True(await server.Server.RevokeAgentAsync(revoked.Id, cancellationToken));
        server.Clock.Advance(hold);

        AssertBases(next.ReadPollAnswer(poll, await answer), held: [], claimed: ["accounting"]);

        OutpostPollAnswer nextAnswer = await server.PollToTheEndOfItsHoldAsync(
            next,
            [ReadOnly("accounting")],
            cancellationToken);
        AssertBases(nextAnswer, held: ["accounting"], claimed: []);

        IReadOnlyList<OutpostAgent> agents = await server.Server.GetAgentsAsync(cancellationToken);
        OutpostAgent listedRevoked = agents.Single(agent => agent.Id == revoked.Id);
        Assert.Equal(OutpostAgentStateEnum.Revoked, listedRevoked.State);
        Assert.Equal<OutpostBase>(
            [new OutpostBase("accounting", ReadOpen: true, WriteOpen: false, Held: false)],
            listedRevoked.Bases);
    }

    // An ordinary answer, with the keys of the poll in its two lists.
    private static void AssertBases(OutpostPollAnswer answer, string[] held, string[] claimed)
    {
        Assert.Null(answer.Newest);
        Assert.Equal(held, answer.Held);
        Assert.Equal(claimed, answer.Claimed);
    }

    private static Task<OutpostReleaseResultEnum> ReleaseAsync(
        LoopbackServer server,
        CancellationToken cancellationToken)
    {
        return server.Server.ReleaseBaseAsync("accounting", cancellationToken);
    }

    private Task<LoopbackServer> StartAsync(string schemaName, CancellationToken cancellationToken)
    {
        return LoopbackServer.StartAsync(
            postgres,
            schemaName,
            options => options.PollTimeout = TimeSpan.FromSeconds(1),
            cancellationToken);
    }
}
