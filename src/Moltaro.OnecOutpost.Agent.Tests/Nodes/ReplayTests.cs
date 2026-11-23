using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Tests.EndToEnd;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using static Moltaro.OnecOutpost.Agent.Tests.Nodes.NodeTraffic;

namespace Moltaro.OnecOutpost.Agent.Tests.Nodes;

// The replay that §13 of docs/protocol.md describes, end to end over two nodes: a hop keeps a poll it captured while
// the agent's clock was ahead, with a high number and a late time, and replays it after the agent was restarted with
// its clock set back and ran a Write. Were that poll the newest, its list and its time would make the Write Expired
// (rules 7, 11), free its caller key, and let a repeat run it a second time. The reservations of the audit log keep
// every poll after the restart above it (§6.2), so it is older and changes nothing (rule 10).
[Trait("Requires", "PostgreSQL")]
public sealed class ReplayTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // How far the clock of the first run is ahead of the nodes': the captured poll's time is then past the Write's take
    // deadline plus the skew, and its number above every number that the clock set back gives.
    private static readonly TimeSpan Ahead = TimeSpan.FromHours(1);

    // The first run's first poll reaches the first node and ends at its hold, which ends the counting of a new pairing
    // (§6.2), so its next poll is numbered from its clock; the proxy captures that one, and it reaches no node. The
    // second run has the nodes' clock. The proxy holds back the poll after the one that took the Write, which lists
    // it, and the Write's outcome, so the nodes know of the Write only that it was handed out. The captured poll is
    // replayed to the second node, and the outcome released to it.
    [Fact]
    public async Task A_poll_captured_while_the_clock_was_ahead_and_replayed_after_a_restart_with_the_clock_set_back_is_older_and_the_Write_is_not_Expired()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TwoNodes nodes = await TwoNodes.StartAsync(postgres, "nodes_replay_of_13", _ => { }, cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(nodes.First, proxy, cancellationToken);
        proxy.Node = nodes.First;
        TimeSpan pollTimeout = nodes.First.Options.PollTimeout;
        DateTimeOffset start = nodes.Clock.GetUtcNow();
        ProxiedRequest captured;

        await using (HostRun ahead = agent.Start(new FakeClock(start + Ahead)))
        {
            await nodes.First.WaitForTimerAsync(start + pollTimeout, cancellationToken);
            Task<ProxiedRequest> capture = proxy.DropNextRequestAsync(IsPoll, cancellationToken);
            nodes.Clock.Advance(pollTimeout);
            captured = await capture;
            Assert.Equal(AgentExitCodes.Stopped, await ahead.StopAsync());
        }

        await using HostRun run = agent.Start(nodes.Clock);
        DateTimeOffset restarted = nodes.Clock.GetUtcNow();
        await nodes.First.WaitForTimerAsync(restarted + pollTimeout, cancellationToken);
        Task<HeldRequest> nextHeld = proxy.HoldNextAsync(IsPoll, cancellationToken);
        Task<HeldRequest> outcomeHeld = proxy.HoldNextAsync(IsOutcome, cancellationToken);

        Task<OutpostOutcome> call = await nodes.Second.StartCallAsync(
            agent.Id,
            () => TestCalls.CallAsync(nodes.Second.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        HeldRequest next = await nextHeld;
        HeldRequest outcome = await outcomeHeld;
        OutpostPollAnswer replayed = ReadPollAnswer(agent, await proxy.ReplayAsync(captured, nodes.Second, cancellationToken));
        Assert.Equal(200, (await outcome.ReleaseAsync(nodes.Second, cancellationToken)).Status);
        OutpostOutcome written = await call;

        ProxiedRequest newest = ProxiedPolls(proxy)[2];
        Assert.True(Number(captured) > restarted.UtcTicks, "The captured poll is not above the numbers of the clock set back.");
        Assert.True(Number(captured) < Number(newest));
        Assert.Equal(Number(newest), replayed.Newest);
        Assert.Empty(replayed.Commands);
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, written.Kind);
        Guid id = written.CommandId!.Value;
        Assert.Contains(id, Received(next.Request));
        await agent.Data.WaitForAsync(Operations.Is($"confirmed {id}"));
        Assert.Single(agent.Workers.Processes.SelectMany(process => process.Commands));
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }
}
