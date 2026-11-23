using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Tests.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using static Moltaro.OnecOutpost.TestSupport.Loopback.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Signals;

// The node and a Signals host that runs apart, over loopback HTTP and TCP (concept §4, S5 brief "The signals"): while
// the host is down, a set of the node gives Unavailable, which the node ignores, and is lost. A waiting poll or call
// reads the store again once its subscription rejoins a host that started on the same port, and every wait of a call
// ends by PollTimeout after it starts, so a set that was lost costs a call at most that. The fake clock of the node
// moves only where a test says so.
[Trait("Requires", "PostgreSQL")]
public sealed class SignalHostTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // The outcome comes while the host is down, so its set is lost; the clock does not move.
    [Fact]
    public async Task A_waiting_call_reads_the_store_again_when_its_subscription_rejoins_a_restarted_host()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using RemoteSignals signals = await RemoteSignals.StartAsync(cancellationToken);
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "signals_call_rejoins",
            _ => { },
            signals.Client,
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        (Task<OutpostOutcome> write, OutpostCommandMessage command) = await HandOutWriteAsync(
            server,
            agent,
            cancellationToken);

        await signals.StopHostAsync();
        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("""{"rows":3}"""), cancellationToken);
        signals.StartHost();

        OutpostOutcome outcome = await write;

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(command.Id, outcome.CommandId);
    }

    // The call creates its command while the host is down, so the set of the agent's key is lost; the clock does not
    // move. The call sets the key before it waits, and its wait arms a timer next to that of the hold, which tells that
    // the set was made. Once the host is back, the held poll takes the command, and the outcome reaches the call.
    [Fact]
    public async Task A_held_poll_reads_the_store_again_when_its_subscription_rejoins_a_restarted_host()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using RemoteSignals signals = await RemoteSignals.StartAsync(cancellationToken);
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "signals_poll_rejoins",
            _ => { },
            signals.Client,
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        DateTimeOffset start = server.Clock.GetUtcNow();
        OutpostPollRequest poll = agent.NextPoll(server.Options.PollTimeout, [ReadOnly(TestCalls.BaseKey)]);
        Task<AgentAnswer> held = await server.HoldPollAsync(agent, poll, cancellationToken);

        await signals.StopHostAsync();
        Task<OutpostOutcome> read = TestCalls.CallAsync(server.Server, "read", null, cancellationToken);
        await server.WaitForTimersAsync(start + server.Options.PollTimeout, 2, cancellationToken);
        signals.StartHost();

        OutpostCommandMessage command = Assert.Single(agent.ReadCommands(agent.ReadPollAnswer(poll, await held)));
        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("""{"rows":3}"""), cancellationToken);
        OutpostOutcome outcome = await read;

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(command.Id, outcome.CommandId);
    }

    // The host stays down, so nothing wakes the call: its wait ends PollTimeout after it started, at the timer that it
    // armed when it began to wait, and its pass finds the outcome. The command is due long after that.
    [Fact]
    public async Task A_call_whose_set_was_lost_returns_its_outcome_once_its_wait_reaches_PollTimeout()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using RemoteSignals signals = await RemoteSignals.StartAsync(cancellationToken);
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "signals_lost_set",
            _ => { },
            signals.Client,
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        DateTimeOffset start = server.Clock.GetUtcNow();
        (Task<OutpostOutcome> write, OutpostCommandMessage command) = await HandOutWriteAsync(
            server,
            agent,
            cancellationToken);
        await server.WaitForTimerAsync(start + server.Options.PollTimeout, cancellationToken);

        await signals.StopHostAsync();
        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("""{"rows":3}"""), cancellationToken);
        server.Clock.Advance(server.Options.PollTimeout);

        OutpostOutcome outcome = await write;

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(command.Id, outcome.CommandId);
    }

    private static Task<(Task<OutpostOutcome> Call, OutpostCommandMessage Command)> HandOutWriteAsync(
        LoopbackServer server,
        TestAgent agent,
        CancellationToken cancellationToken)
    {
        return server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
    }
}
