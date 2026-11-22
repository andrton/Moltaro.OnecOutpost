using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using Moltaro.OnecOutpost.TestSupport.Store;
using static Moltaro.OnecOutpost.TestSupport.Loopback.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Polls;

// The hold of a poll over loopback HTTP, with the fake clock (§6.2 of docs/protocol.md): a poll that becomes the
// newest is held for the smaller of the poll timeout it carries and the option, and ends without commands, since none
// exist before S5. How its hold ends decides its answer. Each test waits until the node has armed the timer of the
// hold, which it does once the poll is recorded, before it moves the clock; no other timer of the clock is due within
// the hold.
[Trait("Requires", "PostgreSQL")]
public sealed class HoldTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // One tick before the end of the hold, the timer of the hold is still armed and has not fired: a hold that ended
    // earlier would have left no timer due by its end. At its end the poll is answered.
    [Theory]
    [InlineData(10, 25)]
    [InlineData(25, 5)]
    public async Task A_poll_is_held_for_the_smaller_of_its_poll_timeout_and_the_option_and_ends_without_commands_at_that_time(
        int pollTimeoutSeconds,
        int optionSeconds)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"hold_{pollTimeoutSeconds}_{optionSeconds}",
            options => options.PollTimeout = TimeSpan.FromSeconds(optionSeconds),
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        TimeSpan hold = TimeSpan.FromSeconds(Math.Min(pollTimeoutSeconds, optionSeconds));
        OutpostPollRequest poll = agent.NextPoll(TimeSpan.FromSeconds(pollTimeoutSeconds), [ReadOnly("accounting")]);
        DateTimeOffset start = server.Clock.GetUtcNow();

        Task<AgentAnswer> answer = agent.PollAsync(poll, cancellationToken);
        await server.WaitForTimerAsync(start + hold, cancellationToken);
        server.Clock.Advance(hold - TimeSpan.FromTicks(1));
        await server.WaitForTimerAsync(start + hold, cancellationToken);
        server.Clock.Advance(TimeSpan.FromTicks(1));

        OutpostPollAnswer read = agent.ReadPollAnswer(poll, await answer);

        Assert.Null(read.Newest);
        Assert.Empty(read.Commands);
        Assert.Equal(start + hold, read.Time);
        Assert.Equal(server.Options.PollTimeout, read.PollTimeout);
        Assert.Equal(server.Options.LeaseGrace, read.LeaseGrace);
        Assert.Equal<string>(["accounting"], read.Held);
        Assert.Empty(read.Claimed);
    }

    // The newer poll has the shorter hold, so that the timer of its hold is the only one due by its end, and it is
    // answered first, as the newest. Nodes share nothing, so only the store tells the replaced poll that it is older.
    [Fact]
    public async Task A_held_poll_that_a_newer_one_replaced_is_answered_as_older_when_its_hold_ends()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "hold_replaced",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        TimeSpan replacedHold = TimeSpan.FromSeconds(25);
        TimeSpan newerHold = TimeSpan.FromSeconds(10);
        DateTimeOffset start = server.Clock.GetUtcNow();

        OutpostPollRequest replaced = agent.NextPoll(replacedHold, [ReadOnly("accounting")]);
        Task<AgentAnswer> replacedAnswer = agent.PollAsync(replaced, cancellationToken);
        await server.WaitForTimerAsync(start + replacedHold, cancellationToken);

        OutpostPollRequest newer = agent.NextPoll(newerHold, [ReadOnly("accounting")]);
        Task<AgentAnswer> newerAnswer = agent.PollAsync(newer, cancellationToken);
        await server.WaitForTimerAsync(start + newerHold, cancellationToken);

        server.Clock.Advance(newerHold);
        Assert.Null(agent.ReadPollAnswer(newer, await newerAnswer).Newest);
        server.Clock.Advance(replacedHold - newerHold);

        OutpostPollAnswer read = agent.ReadPollAnswer(replaced, await replacedAnswer);

        Assert.Equal(newer.Number, read.Newest);
        Assert.Empty(read.Commands);
    }

    [Fact]
    public async Task A_poll_held_while_its_agent_is_revoked_ends_in_the_bare_403_when_its_hold_ends()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "hold_revoked",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        TimeSpan hold = server.Options.PollTimeout;
        DateTimeOffset start = server.Clock.GetUtcNow();

        Task<AgentAnswer> answer = agent.PollAsync(agent.NextPoll(hold, [ReadOnly("accounting")]), cancellationToken);
        await server.WaitForTimerAsync(start + hold, cancellationToken);
        Assert.True(await server.Server.RevokeAgentAsync(agent.Id, cancellationToken));
        server.Clock.Advance(hold);

        AgentAnswer ended = await answer;

        Assert.Equal(403, ended.Status);
        Assert.True(ended.IsBare);
    }

    // The clock does not move: the stop of the host ends the hold.
    [Fact]
    public async Task Stopping_the_node_ends_a_held_poll_at_once_with_the_ordinary_answer()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "hold_node_stopped",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        TimeSpan hold = server.Options.PollTimeout;
        OutpostPollRequest poll = agent.NextPoll(hold, [ReadOnly("accounting")]);
        DateTimeOffset start = server.Clock.GetUtcNow();

        Task<AgentAnswer> answer = agent.PollAsync(poll, cancellationToken);
        await server.WaitForTimerAsync(start + hold, cancellationToken);
        Task stopping = server.StopAsync(cancellationToken);

        OutpostPollAnswer read = agent.ReadPollAnswer(poll, await answer);
        await stopping;

        Assert.Null(read.Newest);
        Assert.Empty(read.Commands);
        Assert.Equal(start, read.Time);
        Assert.Equal<string>(["accounting"], read.Held);
    }

    // The client goes away during the hold. The node then writes no answer, and changes nothing more than the poll
    // recorded: the lease stays.
    [Fact]
    public async Task An_aborted_poll_gets_no_answer_written_and_its_lease_stays_as_recorded()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "hold_aborted",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        TimeSpan hold = server.Options.PollTimeout;
        using CancellationTokenSource abort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        DateTimeOffset start = server.Clock.GetUtcNow();

        Task<AgentAnswer> answer = agent.PollAsync(agent.NextPoll(hold, [ReadOnly("accounting")]), abort.Token);
        await server.WaitForTimerAsync(start + hold, cancellationToken);
        SchemaSnapshot recorded = await server.Schema.TakeSnapshotAsync(cancellationToken);
        abort.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answer);
        EndedRequest ended = await server.WaitForEndedRequestAsync("poll", cancellationToken);

        Assert.False(ended.AnswerStarted);
        Assert.Equal(recorded.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }
}
