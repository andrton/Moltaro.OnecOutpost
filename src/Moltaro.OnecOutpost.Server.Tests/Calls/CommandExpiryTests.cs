using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using static Moltaro.OnecOutpost.TestSupport.Loopback.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Calls;

// Concept rule 7 over loopback HTTP, with the fake clock: a command that no poll took ends as Expired at its take
// deadline, by the node's clock; a command that was handed out ends as Expired only when a poll of its agent, whose
// signed time is past the take deadline plus the skew of the command, does not list it (rule 11). The take timeout
// and the skew are short, so that the node's clock passes both well within the waits of the call, which PollTimeout
// bounds.
[Trait("Requires", "PostgreSQL")]
public sealed class CommandExpiryTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly TimeSpan TakeTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(5);

    // The agent's lease runs for the lease grace after the end of the hold, longer than the take timeout, and no poll
    // is held meanwhile. The wait of the call is due at the take deadline.
    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task A_command_that_no_poll_takes_before_its_take_deadline_ends_as_Expired(string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync($"calls_expiry_not_taken_{mode}", cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        await server.PollToTheEndOfItsHoldAsync(agent, [ReadAndWrite(TestCalls.BaseKey)], cancellationToken);
        string? callerKey = mode == "write" ? "invoice-17" : null;

        Task<OutpostOutcome> call = TestCalls.CallAsync(server.Server, mode, callerKey, cancellationToken);
        await server.WaitForTimerAsync(server.Clock.GetUtcNow() + TakeTimeout, cancellationToken);
        server.Clock.Advance(TakeTimeout);

        OutpostOutcome outcome = await call;
        CommandRow row = Assert.Single(await server.Schema.ListCommandsAsync(cancellationToken));

        Assert.Equal(OutpostOutcomeKindEnum.Expired, outcome.Kind);
        Assert.Equal(row.Id, outcome.CommandId);
        Assert.Null(outcome.Code);
        Assert.Equal(OutpostOutcomeKindEnum.Expired, row.OutcomeKind);
        Assert.Equal(CommandPhaseEnum.Waiting, row.Phase);
    }

    // The answer that handed the command out is lost: the agent never lists it. The node's clock passes the take
    // deadline plus the skew, and the call passes the command at 50 seconds and leaves it as it is. Then the agent
    // polls with its own clock behind the node's: at the take deadline plus the skew the command stays, and a
    // millisecond later the poll makes it Expired, which wakes the call.
    [Fact]
    public async Task A_handed_out_command_expires_only_when_a_poll_whose_signed_time_is_past_its_deadline_plus_its_skew_leaves_it_out()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync("calls_expiry_on_the_agent_time", cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        DateTimeOffset start = server.Clock.GetUtcNow();
        TimeSpan pollTimeout = server.Options.PollTimeout;
        (Task<OutpostOutcome> write, OutpostCommandMessage command) = await server.HandOutAsync(
            agent,
            agent.NextPoll(pollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        DateTimeOffset lastAccepted = command.Deadline + command.Skew;

        await server.WaitForTimerAsync(start + pollTimeout, cancellationToken);
        server.Clock.Advance(pollTimeout);
        await server.WaitForTimerAsync(start + (2 * pollTimeout), cancellationToken);
        server.Clock.Advance(pollTimeout);
        await server.WaitForTimerAsync(start + (3 * pollTimeout), cancellationToken);
        Assert.True(server.Clock.GetUtcNow() > lastAccepted);
        await AssertTakenWithoutOutcomeAsync(server, command.Id, cancellationToken);

        // A shorter hold than the next wait of the call, so that the timer of the hold tells that the poll is recorded.
        TimeSpan hold = TimeSpan.FromSeconds(20);
        OutpostPollRequest atTheLimit = agent.NextPoll(hold, [ReadAndWrite(TestCalls.BaseKey)]) with
        {
            Time = lastAccepted,
        };
        await server.HoldPollAsync(agent, atTheLimit, cancellationToken);
        await AssertTakenWithoutOutcomeAsync(server, command.Id, cancellationToken);

        OutpostPollRequest beyond = agent.NextPoll(hold, [ReadAndWrite(TestCalls.BaseKey)]) with
        {
            Time = lastAccepted + TimeSpan.FromMilliseconds(1),
        };
        _ = agent.PollAsync(beyond, cancellationToken);

        OutpostOutcome outcome = await write;
        CommandRow? row = await server.Schema.FindCommandAsync(command.Id, cancellationToken);

        Assert.Equal(OutpostOutcomeKindEnum.Expired, outcome.Kind);
        Assert.Equal(command.Id, outcome.CommandId);
        Assert.Equal(OutpostOutcomeKindEnum.Expired, row?.OutcomeKind);
    }

    private static async Task AssertTakenWithoutOutcomeAsync(
        LoopbackServer server,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        CommandRow? row = await server.Schema.FindCommandAsync(commandId, cancellationToken);

        Assert.NotNull(row);
        Assert.Equal(CommandPhaseEnum.HandedOut, row.Phase);
        Assert.Null(row.OutcomeKind);
    }

    private Task<LoopbackServer> StartAsync(string schemaName, CancellationToken cancellationToken)
    {
        return LoopbackServer.StartAsync(
            postgres,
            schemaName,
            options =>
            {
                options.TakeTimeout = TakeTimeout;
                options.ClockSkew = ClockSkew;
            },
            cancellationToken);
    }
}
