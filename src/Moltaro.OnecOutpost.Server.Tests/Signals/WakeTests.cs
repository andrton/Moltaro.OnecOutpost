using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Tests.Loopback;
using Moltaro.OnecOutpost.Server.Tests.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.Signals;
using static Moltaro.OnecOutpost.Server.Tests.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Signals;

// How the node wakes its waiting polls and calls through Moltaro.Signals, over loopback HTTP with an in-process
// SignalHost (S5 brief, "The signals"): the keys are the prefix of the options and the identity of an agent or a
// command, and a set wakes every waiter of its key. The clock does not move, so a poll or a call that returns was woken
// by a set, never by the end of its wait.
[Trait("Requires", "PostgreSQL")]
public sealed class WakeTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // A prefix of two segments, not the default, watched by members of the test's own groups: the agent's key is set
    // when a call creates a command for it, and the command's key when its outcome is kept.
    [Fact]
    public async Task The_keys_of_the_signals_are_the_prefix_of_the_options_and_the_identity_of_the_agent_or_the_command()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "signals_keys_of_the_prefix",
            options => options.SignalKeyPrefix = "tests.outpost",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(LoopbackServer.Deadline);
        OutpostPollRequest poll = agent.NextPoll(server.Options.PollTimeout, [ReadOnly(TestCalls.BaseKey)]);
        Task<AgentAnswer> held = await server.HoldPollAsync(agent, poll, cancellationToken);
        using ISignalSubscription agentKey = Watch(server, "tests.outpost." + agent.Id.ToString("D"));
        Assert.Equal(SignalWaitResultEnum.Joined, await agentKey.WaitAsync(null, deadline.Token));

        Task<OutpostOutcome> read = TestCalls.CallAsync(server.Server, "read", null, cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Signaled, await agentKey.WaitAsync(null, deadline.Token));

        OutpostCommandMessage command = Assert.Single(agent.ReadCommands(agent.ReadPollAnswer(poll, await held)));
        using ISignalSubscription commandKey = Watch(server, "tests.outpost." + command.Id.ToString("D"));
        Assert.Equal(SignalWaitResultEnum.Joined, await commandKey.WaitAsync(null, deadline.Token));

        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("""{"rows":3}"""), cancellationToken);

        Assert.Equal(SignalWaitResultEnum.Signaled, await commandKey.WaitAsync(null, deadline.Token));
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, (await read).Kind);
    }

    // The second is a repeat with the caller key of the first. Each arms the timer of its wait once it waits, which
    // tells that both wait before the outcome comes.
    [Fact]
    public async Task Two_calls_waiting_on_one_command_both_return_when_its_outcome_is_kept()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "signals_two_waiting_calls",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        DateTimeOffset start = server.Clock.GetUtcNow();
        (Task<OutpostOutcome> first, OutpostCommandMessage command) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        Task<OutpostOutcome> repeat = TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken);
        await server.WaitForTimersAsync(start + server.Options.PollTimeout, 2, cancellationToken);

        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("""{"rows":3}"""), cancellationToken);

        OutpostOutcome[] outcomes = [await first, await repeat];

        Assert.All(outcomes, outcome => Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind));
        Assert.All(outcomes, outcome => Assert.Equal(command.Id, outcome.CommandId));
    }

    // Two polls of one agent are held: the older for the option's poll timeout, the newer, which replaced it, for a
    // shorter one, so that the timer of its hold tells that it is recorded. The call wakes both: the newest takes the
    // command, and the older takes nothing and is answered as older (rule 10, §6.2 of docs/protocol.md).
    [Fact]
    public async Task The_newest_poll_takes_a_new_command_while_an_older_poll_of_its_agent_wakes_and_is_answered_as_older()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "signals_newest_and_older_poll",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostPollRequest older = agent.NextPoll(server.Options.PollTimeout, [ReadOnly(TestCalls.BaseKey)]);
        Task<AgentAnswer> olderAnswer = await server.HoldPollAsync(agent, older, cancellationToken);
        OutpostPollRequest newest = agent.NextPoll(TimeSpan.FromSeconds(10), [ReadOnly(TestCalls.BaseKey)]);
        Task<AgentAnswer> newestAnswer = await server.HoldPollAsync(agent, newest, cancellationToken);

        Task<OutpostOutcome> read = TestCalls.CallAsync(server.Server, "read", null, cancellationToken);

        OutpostPollAnswer taken = agent.ReadPollAnswer(newest, await newestAnswer);
        OutpostPollAnswer answeredAsOlder = agent.ReadPollAnswer(older, await olderAnswer);

        Assert.Null(taken.Newest);
        OutpostCommandMessage command = Assert.Single(agent.ReadCommands(taken));
        Assert.Equal(newest.Number, answeredAsOlder.Newest);
        Assert.Empty(answeredAsOlder.Commands);

        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("null"), cancellationToken);
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, (await read).Kind);
    }

    // A draft of the public contract (S5 brief): a held poll whose agent was revoked gets the bare 403 of §9 also when
    // it wakes. A revocation sets no agent's key, so the test sets it, as a node that created a command for the agent
    // just before the revocation would. The set finds the poll's member, its only one.
    [Fact]
    public async Task A_held_poll_whose_agent_was_revoked_gets_the_bare_403_when_it_wakes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "signals_revoked_poll_wakes",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostPollRequest poll = agent.NextPoll(server.Options.PollTimeout, [ReadOnly(TestCalls.BaseKey)]);
        Task<AgentAnswer> held = await server.HoldPollAsync(agent, poll, cancellationToken);
        Assert.True(await server.Server.RevokeAgentAsync(agent.Id, cancellationToken));

        SignalSetResult set = await server.Signals.SetAsync(server.SignalKey(agent.Id), cancellationToken);
        AgentAnswer answer = await held;

        Assert.Equal(1, set.Groups);
        Assert.Equal(403, answer.Status);
        Assert.True(answer.IsBare);
    }

    // A member of a group of the test's own on the key.
    private static ISignalSubscription Watch(LoopbackServer server, string key)
    {
        return server.Signals.Subscribe(key, Guid.NewGuid().ToString("N"));
    }
}
