using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Tests.EndToEnd;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using static Moltaro.OnecOutpost.Agent.Tests.Nodes.NodeTraffic;

namespace Moltaro.OnecOutpost.Agent.Tests.Nodes;

// Messages lost or held on the path between the agent and two nodes, as a hop that opens TLS can lose or hold them
// (§13 of docs/protocol.md, "What a hop on the path can still do"): the test proxy drops a request or its answer, holds
// a request and releases it to a node, or replays one. A command still runs once on its agent, an older poll takes
// nothing, an outcome counts once, and a command that was handed out expires only by the agent's own signed time
// (concept rules 7, 10 and 11). The agent and both nodes share the fake clock, which moves only where a test says so.
[Trait("Requires", "PostgreSQL")]
public sealed class LostMessageTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // The proxy drops the answer of the first poll, on the first node, which handed out the Write of a call on the
    // second: the agent never received it. After its first delay the agent polls the second node. That poll is newer
    // and does not list the Write, so it hands it out again (rule 11), and the agent runs it once.
    [Fact]
    public async Task A_command_whose_poll_answer_was_lost_is_handed_out_again_and_runs_once()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TwoNodes nodes = await TwoNodes.StartAsync(postgres, "nodes_lost_poll_answer", _ => { }, cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(nodes.First, proxy, cancellationToken);
        proxy.Node = nodes.First;
        Task<AgentAnswer> dropped = proxy.DropNextAnswerAsync(IsPoll, cancellationToken);

        await using HostRun run = agent.Start(nodes.Clock);
        DateTimeOffset start = nodes.Clock.GetUtcNow();
        await nodes.First.WaitForTimerAsync(start + nodes.First.Options.PollTimeout, cancellationToken);

        Task<OutpostOutcome> call = await nodes.Second.StartCallAsync(
            agent.Id,
            () => TestCalls.CallAsync(nodes.Second.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        Guid id = Assert.Single(CommandsOf(agent, await dropped)).Id;
        proxy.Node = nodes.Second;
        await nodes.Second.WaitForTimerAsync(start + TestTimes.FirstDelay, cancellationToken);
        nodes.Clock.Advance(TestTimes.FirstDelay);

        OutpostOutcome outcome = await call;

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(id, outcome.CommandId);
        Assert.Empty(Received(ProxiedPolls(proxy)[1]));
        await agent.Data.WaitForAsync(Operations.Is($"confirmed {id}"));
        Assert.Single(Operations.Since(agent.Data, 0), operation => operation == $"received {id}");
        Assert.Single(agent.Workers.Processes.SelectMany(process => process.Commands));
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }

    // The proxy holds the agent's first poll until the agent gives up on it, after its poll timeout plus the lease
    // grace (§6.2), and polls again after its first delay. That poll reaches the first node, becomes the newest and
    // ends at its hold, and the proxy holds the one after it. A call on the second node then creates a Write while no
    // poll waits on any node: the agent is connected by the lease of the newest poll. Released to a node, the first
    // poll is older, and so is the newest replayed exactly: each is answered at once with the newest number and takes
    // nothing (rule 10). The held poll after the newest takes the Write when it is released, and its answer reaches the
    // agent, which still waits on it.
    [Fact]
    public async Task An_older_poll_held_and_released_and_an_exact_replay_of_the_newest_take_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TwoNodes nodes = await TwoNodes.StartAsync(postgres, "nodes_older_polls", _ => { }, cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(nodes.First, proxy, cancellationToken);
        proxy.Node = nodes.First;
        TimeSpan pollTimeout = nodes.First.Options.PollTimeout;
        TimeSpan agentWait = pollTimeout + nodes.First.Options.LeaseGrace;
        Task<HeldRequest> firstHeld = proxy.HoldNextAsync(IsPoll, cancellationToken);

        await using HostRun run = agent.Start(nodes.Clock);
        DateTimeOffset start = nodes.Clock.GetUtcNow();
        HeldRequest older = await firstHeld;
        await nodes.First.WaitForTimerAsync(start + agentWait, cancellationToken);
        nodes.Clock.Advance(agentWait);
        await nodes.First.WaitForTimerAsync(start + agentWait + TestTimes.FirstDelay, cancellationToken);
        nodes.Clock.Advance(TestTimes.FirstDelay);
        await nodes.First.WaitForTimerAsync(start + agentWait + TestTimes.FirstDelay + pollTimeout, cancellationToken);
        Task<HeldRequest> nextHeld = proxy.HoldNextAsync(IsPoll, cancellationToken);
        nodes.Clock.Advance(pollTimeout);
        HeldRequest next = await nextHeld;

        Task<OutpostOutcome> call = await nodes.Second.StartCallAsync(
            agent.Id,
            () => TestCalls.CallAsync(nodes.Second.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        ProxiedRequest newest = ProxiedPolls(proxy)[1];
        OutpostPollAnswer olderAnswer = ReadPollAnswer(agent, await older.ReleaseAsync(nodes.Second, cancellationToken));
        OutpostPollAnswer replayAnswer = ReadPollAnswer(agent, await proxy.ReplayAsync(newest, nodes.First, cancellationToken));
        AgentAnswer taken = await next.ReleaseAsync(nodes.Second, cancellationToken);
        OutpostOutcome outcome = await call;

        Assert.True(Number(older.Request) < Number(newest));
        Assert.Equal(Number(newest), olderAnswer.Newest);
        Assert.Empty(olderAnswer.Commands);
        Assert.Equal(Number(newest), replayAnswer.Newest);
        Assert.Empty(replayAnswer.Commands);
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Guid id = outcome.CommandId!.Value;
        Assert.Equal([id], CommandsOf(agent, taken).Select(command => command.Id));
        await agent.Data.WaitForAsync(Operations.Is($"confirmed {id}"));
        Assert.Single(agent.Workers.Processes.SelectMany(process => process.Commands));
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }

    // The first node keeps the outcome of the Write, which wakes the call on the second node, and the proxy drops the
    // confirmation. After the first delay of its outcomes the agent sends the same bytes again, now to the second
    // node, which confirms them and changes nothing (rule 11): the Write ran once and has one outcome, which a repeat
    // of the call with its caller key returns.
    [Fact]
    public async Task An_outcome_whose_confirmation_was_lost_is_sent_again_and_counted_once()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TwoNodes nodes = await TwoNodes.StartAsync(postgres, "nodes_lost_outcome", _ => { }, cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(nodes.First, proxy, cancellationToken);
        proxy.Node = nodes.First;
        Task<AgentAnswer> dropped = proxy.DropNextAnswerAsync(IsOutcome, cancellationToken);

        await using HostRun run = agent.Start(nodes.Clock);
        DateTimeOffset start = nodes.Clock.GetUtcNow();
        await nodes.First.WaitForTimerAsync(start + nodes.First.Options.PollTimeout, cancellationToken);

        Task<OutpostOutcome> call = await nodes.Second.StartCallAsync(
            agent.Id,
            () => TestCalls.CallAsync(nodes.Second.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        OutpostOutcome outcome = await call;
        Guid id = outcome.CommandId!.Value;
        Assert.Equal(200, (await dropped).Status);
        proxy.Node = nodes.Second;
        await nodes.Second.WaitForTimerAsync(start + TestTimes.FirstDelay, cancellationToken);
        nodes.Clock.Advance(TestTimes.FirstDelay);
        await agent.Data.WaitForAsync(Operations.Is($"confirmed {id}"));

        OutpostOutcome repeat = await TestCalls.CallAsync(nodes.Second.Server, "write", "invoice-17", cancellationToken);

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, repeat.Kind);
        Assert.Equal(id, repeat.CommandId);
        Assert.Equal(TestCalls.Parameters, repeat.Result!.Value.GetRawText());
        List<ProxiedRequest> sent = ProxiedOutcomes(proxy);
        Assert.Equal(2, sent.Count);
        Assert.Equal(sent[0].Body, sent[1].Body);
        List<string> operations = Operations.Since(agent.Data, 0);
        Assert.Single(operations, operation => operation == $"ended {id}");
        Assert.Single(operations, operation => operation == $"confirmed {id}");
        Assert.Single(agent.Workers.Processes.SelectMany(process => process.Commands));
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }

    // The take timeout and the skew are short, so that the nodes' clock passes the take deadline plus the skew while
    // the agent still waits on the poll that the proxy holds back. The proxy drops the answer of the first poll, on the
    // first node, which handed out the Write of a call on the second. The next poll, on the second node, does not list
    // the Write: its agent time is before the deadline plus the skew, so the Write stays taken, and the deadline has not
    // passed, so the poll hands the Write out again (rules 7, 11). The agent accepts it by its own clock and runs it.
    // The proxy holds back the agent's next poll, which lists the Write, and its outcome, so the nodes learn of
    // neither. The call's wait ends at the take deadline, and its pass leaves the Write taken; past the deadline plus
    // the skew, a repeat with the caller key on the first node follows the Write, since no poll whose agent time is
    // past it left the Write out (rule 7). Released to the first node, the outcome reaches both.
    [Fact]
    public async Task A_command_delivered_in_a_later_answer_after_a_lost_one_stays_taken_past_its_deadline_plus_the_skew_while_the_next_poll_is_held_back()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TwoNodes nodes = await TwoNodes.StartAsync(
            postgres,
            "nodes_delivered_later",
            options =>
            {
                options.TakeTimeout = TimeSpan.FromSeconds(5);
                options.ClockSkew = TimeSpan.FromSeconds(5);
            },
            cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(nodes.First, proxy, cancellationToken);
        proxy.Node = nodes.First;
        TimeSpan pollTimeout = nodes.First.Options.PollTimeout;
        Task<AgentAnswer> dropped = proxy.DropNextAnswerAsync(IsPoll, cancellationToken);

        await using HostRun run = agent.Start(nodes.Clock);
        DateTimeOffset start = nodes.Clock.GetUtcNow();
        await nodes.First.WaitForTimerAsync(start + pollTimeout, cancellationToken);

        Task<OutpostOutcome> call = await nodes.Second.StartCallAsync(
            agent.Id,
            () => TestCalls.CallAsync(nodes.Second.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        OutpostCommandMessage command = Assert.Single(CommandsOf(agent, await dropped));
        DateTimeOffset lastAccepted = command.Deadline + command.Skew;
        proxy.Node = nodes.Second;
        Task<HeldRequest> nextHeld = proxy.HoldNextAsync(PollListing(command.Id), cancellationToken);
        Task<HeldRequest> outcomeHeld = proxy.HoldNextAsync(IsOutcome, cancellationToken);
        await nodes.Second.WaitForTimerAsync(start + TestTimes.FirstDelay, cancellationToken);
        nodes.Clock.Advance(TestTimes.FirstDelay);
        HeldRequest next = await nextHeld;
        HeldRequest outcome = await outcomeHeld;

        nodes.Clock.Advance(command.Deadline - nodes.Clock.GetUtcNow());
        await nodes.Second.WaitForTimerAsync(command.Deadline + pollTimeout, cancellationToken);
        nodes.Clock.Advance(lastAccepted + TestTimes.Millisecond - nodes.Clock.GetUtcNow());
        Task<OutpostOutcome> repeat = TestCalls.CallAsync(nodes.First.Server, "write", "invoice-17", cancellationToken);

        // Two timers are due by the end of the repeat's wait: the call's next wait and that wait, armed once it waits.
        await nodes.First.WaitForTimersAsync(nodes.Clock.GetUtcNow() + pollTimeout, 2, cancellationToken);
        Assert.False(call.IsCompleted, "The call returned while its Write stayed taken.");
        Assert.False(repeat.IsCompleted, "The repeat returned while the Write stayed taken.");

        Assert.Equal(200, (await outcome.ReleaseAsync(nodes.First, cancellationToken)).Status);
        OutpostOutcome first = await call;
        OutpostOutcome second = await repeat;

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, first.Kind);
        Assert.Equal(command.Id, first.CommandId);
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, second.Kind);
        Assert.Equal(command.Id, second.CommandId);
        Assert.Empty(Received(ProxiedPolls(proxy)[1]));
        Assert.Contains(command.Id, Received(next.Request));
        await agent.Data.WaitForAsync(Operations.Is($"confirmed {command.Id}"));
        Assert.Single(Operations.Since(agent.Data, 0), operation => operation == $"received {command.Id}");
        Assert.Single(agent.Workers.Processes.SelectMany(process => process.Commands));
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }
}
