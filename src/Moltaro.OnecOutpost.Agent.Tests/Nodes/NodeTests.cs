using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Tests.EndToEnd;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Server;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using Moltaro.Signals;
using static Moltaro.OnecOutpost.Agent.Tests.Nodes.NodeTraffic;

namespace Moltaro.OnecOutpost.Agent.Tests.Nodes;

// Two nodes of one server on one PostgreSQL, the Signals host embedded in the first and a client of it in the second,
// the agent in the process with the fake workers, and the test proxy between them, whose Node says which node takes
// each request (the S7 brief, "With two server nodes"). Nodes share nothing in memory (principle 6): a call on one node
// reaches a poll held on the other through the store and the signals; a node that stops loses nothing; and while the
// Signals host is down, every wait ends by a deadline of its own and reads the store (concept §4). Agent and nodes share
// the fake clock, which moves only where a test says so.
[Trait("Requires", "PostgreSQL")]
public sealed class NodeTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // The clock does not move, so the call returns because each node woke the other (principle 6): the call's set of the
    // agent's key the poll held on the other node, and that node's set of the command's key, once it kept the outcome,
    // the call.
    [Theory]
    [InlineData("first")]
    [InlineData("second")]
    public async Task A_call_on_one_node_wakes_the_poll_held_on_the_other_and_its_outcome_wakes_the_call(string pollNode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TwoNodes nodes = await TwoNodes.StartAsync(
            postgres,
            $"nodes_call_and_poll_{pollNode}",
            _ => { },
            cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(nodes.First, proxy, cancellationToken);
        LoopbackServer polled = pollNode == "first" ? nodes.First : nodes.Second;
        LoopbackServer called = pollNode == "first" ? nodes.Second : nodes.First;
        proxy.Node = polled;

        await using HostRun run = agent.Start(nodes.Clock);
        await polled.WaitForTimerAsync(nodes.Clock.GetUtcNow() + polled.Options.PollTimeout, cancellationToken);

        Task<OutpostOutcome> call = await called.StartCallAsync(
            agent.Id,
            () => TestCalls.CallAsync(called.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        OutpostOutcome outcome = await call;

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(TestCalls.Parameters, outcome.Result!.Value.GetRawText());
        Guid id = outcome.CommandId!.Value;
        await agent.Data.WaitForAsync(Operations.Is($"confirmed {id}"));
        Assert.Single(agent.Workers.Processes.SelectMany(process => process.Commands));
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }

    // The poll waits on the second node, which stops: the poll ends, and the agent polls again, at once after the
    // stopping node's answer, or after its first delay when no answer came (§9). The proxy sends the next poll to the
    // first node and holds it, so that a call on the first node creates its Write while no poll waits on any node; the
    // agent is connected by the lease of the poll on the stopped node. Released to the first node, the held poll takes
    // the Write, and its answer reaches the agent, which still waits on it.
    [Fact]
    public async Task A_poll_waiting_on_a_node_that_stops_ends_and_the_next_poll_on_the_other_node_takes_the_command_created_meanwhile()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TwoNodes nodes = await TwoNodes.StartAsync(postgres, "nodes_stopped_node", _ => { }, cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(nodes.First, proxy, cancellationToken);
        proxy.Node = nodes.Second;

        await using HostRun run = agent.Start(nodes.Clock);
        await nodes.Second.WaitForTimerAsync(nodes.Clock.GetUtcNow() + nodes.Second.Options.PollTimeout, cancellationToken);

        proxy.Node = nodes.First;
        Task<HeldRequest> next = proxy.HoldNextAsync(IsPoll, cancellationToken);
        await nodes.StopAsync(nodes.Second, cancellationToken);
        HeldRequest held = await AfterTheEndOfThePollAsync(nodes.Clock, next, cancellationToken);

        Task<OutpostOutcome> call = await nodes.First.StartCallAsync(
            agent.Id,
            () => TestCalls.CallAsync(nodes.First.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        AgentAnswer taken = await held.ReleaseAsync(nodes.First, cancellationToken);
        OutpostOutcome outcome = await call;

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Guid id = outcome.CommandId!.Value;
        Assert.Equal([id], CommandsOf(agent, taken).Select(command => command.Id));
        await agent.Data.WaitForAsync(Operations.Is($"confirmed {id}"));
        Assert.Single(agent.Workers.Processes.SelectMany(process => process.Commands));
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }

    // The poll waits on the second node, and the first stops with the Signals host it embeds, which the second's client
    // reports. Five seconds into the poll's hold, a call on the second node creates a Write, whose set is lost: the poll
    // is not woken, and ends at its timeout. The next poll takes the Write at once, and its outcome is kept while the
    // call, which nothing can wake, waits until its wait ends, PollTimeout after it began, and then reads the outcome.
    // Then the first node starts again with a new host on the old port, the client reaches it by itself, and a call on
    // the first node wakes the poll held on the second again (concept §4).
    [Fact]
    public async Task While_the_signals_host_is_stopped_a_poll_ends_at_its_timeout_a_call_at_its_deadline_and_nothing_is_lost()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TwoNodes nodes = await TwoNodes.StartAsync(postgres, "nodes_signals_host_stopped", _ => { }, cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(nodes.First, proxy, cancellationToken);
        proxy.Node = nodes.Second;
        TimeSpan pollTimeout = nodes.Second.Options.PollTimeout;
        TimeSpan intoTheHold = TimeSpan.FromSeconds(5);

        await using HostRun run = agent.Start(nodes.Clock);
        DateTimeOffset start = nodes.Clock.GetUtcNow();
        await nodes.Second.WaitForTimerAsync(start + pollTimeout, cancellationToken);

        Task disconnected = RemoteSignals.WhenStateAsync(
            nodes.Second.Signals,
            SignalConnectionStateEnum.Disconnected,
            cancellationToken);
        await nodes.StopAsync(nodes.First, cancellationToken);
        await disconnected;

        nodes.Clock.Advance(intoTheHold);
        Task<OutpostOutcome> call = TestCalls.CallAsync(nodes.Second.Server, "write", "invoice-17", cancellationToken);

        // Two timers are due by the end of the call's wait: the poll's hold and that wait, armed once the call waits.
        await nodes.Second.WaitForTimersAsync(start + intoTheHold + pollTimeout, 2, cancellationToken);
        Assert.DoesNotContain(Operations.Since(agent.Data, 0), operation => operation.StartsWith("received ", StringComparison.Ordinal));

        nodes.Clock.Advance(pollTimeout - intoTheHold);
        await agent.Data.WaitForAsync(change => Operations.Word(change).StartsWith("confirmed ", StringComparison.Ordinal));
        Assert.False(call.IsCompleted, "The call returned before its wait ended, with the Signals host stopped.");

        nodes.Clock.Advance(intoTheHold);
        OutpostOutcome outcome = await call;

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        await agent.Data.WaitForAsync(Operations.Is($"confirmed {outcome.CommandId}"));
        Assert.Single(agent.Workers.Processes.SelectMany(process => process.Commands));

        Task connected = RemoteSignals.WhenStateAsync(
            nodes.Second.Signals,
            SignalConnectionStateEnum.Connected,
            cancellationToken);
        LoopbackServer first = await nodes.RestartFirstAsync(cancellationToken);
        await connected;

        // A Read: the poll held now was sent while the Write held the agent's one write worker.
        Task<OutpostOutcome> read = await first.StartCallAsync(
            agent.Id,
            () => TestCalls.CallAsync(first.Server, "read", null, cancellationToken),
            cancellationToken);
        OutpostOutcome readOutcome = await read;

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, readOutcome.Kind);
        Assert.Equal(2, agent.Workers.Processes.SelectMany(process => process.Commands).Count());
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }

    // The held next poll, once the agent has sent it: at once after a poll that ended with an answer, or after the
    // agent's first delay after one that ended without, which the test lets pass once the agent waits for it.
    private static async Task<HeldRequest> AfterTheEndOfThePollAsync(
        FakeClock clock,
        Task<HeldRequest> next,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task delayed = clock.WaitForTimerAsync(clock.GetUtcNow() + TestTimes.FirstDelay, stop.Token);

        if (await Task.WhenAny(next, delayed) == delayed)
        {
            clock.Advance(TestTimes.FirstDelay);
        }

        stop.Cancel();

        return await next;
    }
}
