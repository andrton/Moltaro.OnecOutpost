using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using Moltaro.OnecOutpost.TestSupport.Store;
using static Moltaro.OnecOutpost.TestSupport.Loopback.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Calls;

// The checks of ReadAsync and WriteAsync after their arguments, over loopback HTTP, in their order (IOutpostServer):
// the caller key, then the base, then the agent (concept rules 4 and 6). A check that refuses creates no command, and
// its outcome names none. Each test makes the later checks fail too, so that a check made out of its order shows.
[Trait("Requires", "PostgreSQL")]
public sealed class CallOrderTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task A_call_to_a_base_that_no_agent_holds_is_refused_with_unknown_base_and_creates_no_command(string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"calls_order_unknown_base_{mode}",
            cancellationToken);
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        OutpostOutcome outcome = await TestCalls.CallAsync(server.Server, mode, "invoice-17", cancellationToken);

        AssertRefusedWithoutCommand(outcome, OutpostCodes.UnknownBase);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // The holder is away, so a call that reached the check of the agent would get Unavailable, as the Read does.
    [Fact]
    public async Task A_write_to_a_base_its_holder_opens_for_read_only_is_refused_with_mode_closed_even_while_the_holder_is_away()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "calls_order_write_closed",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        await server.PollToTheEndOfItsHoldAsync(agent, [ReadOnly(TestCalls.BaseKey)], cancellationToken);
        server.Clock.Advance(server.Options.LeaseGrace);
        Assert.False(await server.IsConnectedAsync(agent.Id, cancellationToken));
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        OutpostOutcome write = await TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken);
        OutpostOutcome read = await TestCalls.CallAsync(server.Server, "read", "report-17", cancellationToken);

        AssertRefusedWithoutCommand(write, OutpostCodes.ModeClosed);
        Assert.Equal(OutpostOutcomeKindEnum.Unavailable, read.Kind);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // The agent still holds the key, which its newest poll no longer
    // declares, so the key opens neither mode. The agent is connected.
    [Fact]
    public async Task A_call_to_a_key_that_the_newest_poll_of_its_holder_no_longer_declares_is_refused_with_mode_closed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "calls_order_key_not_declared",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        await server.PollToTheEndOfItsHoldAsync(agent, [ReadAndWrite(TestCalls.BaseKey)], cancellationToken);
        OutpostPollRequest newest = agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite("warehouse")]);
        await server.HoldPollAsync(agent, newest, cancellationToken);
        Assert.Equal(agent.Id, await server.Schema.FindHolderAsync(TestCalls.BaseKey, cancellationToken));
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        OutpostOutcome read = await TestCalls.CallAsync(server.Server, "read", null, cancellationToken);
        OutpostOutcome write = await TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken);

        AssertRefusedWithoutCommand(read, OutpostCodes.ModeClosed);
        AssertRefusedWithoutCommand(write, OutpostCodes.ModeClosed);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // Concept rule 6. The lease of the last poll ended at the end of its hold plus the grace. The Read has a caller key
    // too, which names no command.
    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task A_call_to_a_base_whose_agent_is_not_connected_returns_Unavailable_at_once_and_creates_no_command(
        string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"calls_order_unavailable_{mode}",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        await server.PollToTheEndOfItsHoldAsync(agent, [ReadAndWrite(TestCalls.BaseKey)], cancellationToken);
        server.Clock.Advance(server.Options.LeaseGrace);
        Assert.False(await server.IsConnectedAsync(agent.Id, cancellationToken));
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        OutpostOutcome outcome = await TestCalls.CallAsync(server.Server, mode, "key-17", cancellationToken);

        Assert.Equal(OutpostOutcomeKindEnum.Unavailable, outcome.Kind);
        Assert.Null(outcome.CommandId);
        Assert.Null(outcome.Code);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // Concept rule 4: the caller key is checked before anything else, so a repeat follows the command its key names.
    // Each repeat comes after one more of the later checks would refuse it: the newest poll of the holder no longer
    // declares the key (mode-closed), then the key is released (unknown-base), then the agent is away (Unavailable).
    [Fact]
    public async Task A_repeat_with_the_caller_key_of_a_command_returns_its_outcome_before_any_check_of_its_base_or_its_agent()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "calls_order_caller_key_first",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        (Task<OutpostOutcome> write, OutpostCommandMessage command) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("""{"rows":3}"""), cancellationToken);
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, (await write).Kind);

        await server.PollToTheEndOfItsHoldAsync(agent, [], cancellationToken);
        OutpostOutcome notDeclared = await TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken);

        Assert.Equal(OutpostReleaseResultEnum.Released, await server.Server.ReleaseBaseAsync(TestCalls.BaseKey, cancellationToken));
        OutpostOutcome released = await TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken);

        server.Clock.Advance(server.Options.LeaseGrace);
        Assert.False(await server.IsConnectedAsync(agent.Id, cancellationToken));
        OutpostOutcome away = await TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken);

        OutpostOutcome[] repeats = [notDeclared, released, away];

        Assert.All(repeats, repeat => AssertSucceeded(repeat, command.Id));
        Assert.Single(await server.Schema.ListCommandsAsync(cancellationToken));
    }

    private static void AssertRefusedWithoutCommand(OutpostOutcome outcome, string code)
    {
        Assert.Equal(OutpostOutcomeKindEnum.Refused, outcome.Kind);
        Assert.Equal(code, outcome.Code);
        Assert.Null(outcome.CommandId);
    }

    private static void AssertSucceeded(OutpostOutcome outcome, Guid commandId)
    {
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(commandId, outcome.CommandId);
        Assert.Equal("""{"rows":3}""", outcome.Result!.Value.GetRawText());
    }
}
