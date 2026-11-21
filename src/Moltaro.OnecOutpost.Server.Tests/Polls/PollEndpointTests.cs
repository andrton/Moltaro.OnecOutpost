using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Tests.Loopback;
using Moltaro.OnecOutpost.Server.Tests.Migrations;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Polls;

// The poll over loopback HTTP, with test agents: the refusals that the server signs as the answer to the very body it
// received (§9 of docs/protocol.md), and that an older poll and an exact replay of the newest are answered at once and
// change nothing (concept rule 10, §6.2). An answer that the node held instead would wait for the fake clock, which
// these tests do not move, until the deadline of the test.
[Trait("Requires", "PostgreSQL")]
public sealed class PollEndpointTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // The agent signed one poll, and another body arrives with that signature: the refusal answers the body that came.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_poll_whose_body_changed_after_it_was_signed_gets_the_signed_signature_refusal_and_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "poll_changed_body",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostPollRequest signed = agent.NextPoll(server.Options.PollTimeout, [ReadOnly("accounting")]);
        OutpostPollRequest changed = signed with { Bases = [ReadOnly("warehouse")] };
        byte[] signature = agent.Sign(OutpostRequestKindEnum.Poll, OutpostCodec.Write(signed));
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        AgentAnswer answer = await agent.SendAsync(
            OutpostRequestKindEnum.Poll,
            OutpostCodec.Write(changed),
            signature,
            cancellationToken);

        Assert.Equal(401, answer.Status);
        Assert.Equal(OutpostProtocolCodes.Signature, agent.ReadRefusal(answer).Code);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_poll_signed_with_the_key_of_another_agent_gets_the_signed_signature_refusal_and_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "poll_other_key",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        using TestAgent other = await server.PairAgentAsync(cancellationToken);
        byte[] body = OutpostCodec.Write(agent.NextPoll(server.Options.PollTimeout, [ReadOnly("accounting")]));
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        AgentAnswer answer = await agent.SendAsync(
            OutpostRequestKindEnum.Poll,
            body,
            other.SignAs(agent.Id, OutpostRequestKindEnum.Poll, body),
            cancellationToken);

        Assert.Equal(401, answer.Status);
        Assert.Equal(OutpostProtocolCodes.Signature, agent.ReadRefusal(answer).Code);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // The poll declares one base twice, which §6.2 refuses.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_signed_poll_that_breaks_the_rules_of_its_message_gets_the_signed_form_refusal_and_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "poll_broken_form",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostPollBase[] twice = [ReadOnly("accounting"), ReadOnly("accounting")];
        OutpostPollRequest poll = agent.NextPoll(server.Options.PollTimeout, twice);
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        AgentAnswer answer = await agent.PollAsync(poll, cancellationToken);

        Assert.Equal(400, answer.Status);
        Assert.Equal(OutpostProtocolCodes.Form, agent.ReadRefusal(answer).Code);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // The newest poll is answered when its hold ends. A minute later come an older poll of the agent, which declares
    // another base, and an exact replay of the newest, its bytes and its signature: each is answered at once, as an
    // older poll, and the store is as the newest poll left it.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task An_older_poll_and_an_exact_replay_of_the_newest_are_answered_at_once_signed_with_the_newest_number_and_change_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "poll_older_and_replayed",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostPollRequest older = agent.NextPoll(server.Options.PollTimeout, [ReadOnly("warehouse")]);
        OutpostPollRequest newest = agent.NextPoll(server.Options.PollTimeout, [ReadOnly("accounting")]);
        DateTimeOffset start = server.Clock.GetUtcNow();
        Task<AgentAnswer> held = agent.PollAsync(newest, cancellationToken);
        await server.WaitForTimerAsync(start + server.Options.PollTimeout, cancellationToken);
        server.Clock.Advance(server.Options.PollTimeout);
        AgentAnswer newestAnswer = await held;
        Assert.Null(agent.ReadPollAnswer(newest, newestAnswer).Newest);
        server.Clock.Advance(TimeSpan.FromMinutes(1));
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        AgentAnswer olderAnswer = await agent.PollAsync(older, cancellationToken);
        AgentAnswer replayAnswer = await agent.ReplayAsync(newestAnswer, cancellationToken);

        AssertAnsweredAsOlder(agent.ReadPollAnswer(older, olderAnswer), newest.Number);
        AssertAnsweredAsOlder(agent.ReadPollAnswer(newest, replayAnswer), newest.Number);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // The answer to an older poll names the newest number recorded, holds no commands, and has the lists of the newest
    // poll.
    private static void AssertAnsweredAsOlder(OutpostPollAnswer answer, long newest)
    {
        Assert.Equal(newest, answer.Newest);
        Assert.Empty(answer.Commands);
        Assert.Equal<string>(["accounting"], answer.Held);
        Assert.Empty(answer.Claimed);
    }
}
