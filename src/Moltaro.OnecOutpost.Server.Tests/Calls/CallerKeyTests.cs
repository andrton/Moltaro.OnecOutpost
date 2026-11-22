using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.Server.Tests.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using static Moltaro.OnecOutpost.TestSupport.Loopback.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Calls;

// Concept rule 4 over loopback HTTP: the server accepts one command per base and caller key. A repeat with the same
// mode, script and parameters follows the first command and returns its outcome, the parameters compared as JSON
// values; a repeat with anything else is refused with caller-key-conflict and creates nothing; a first command that
// ended as Expired or Refused frees the key, so that a repeat creates a new command. Whether a repeat runs the script
// again is told by the commands in the store and by the commands that the polls hand out.
[Trait("Requires", "PostgreSQL")]
public sealed class CallerKeyTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string CallerKey = "invoice-17";

    private const string Result = """{"rows":3}""";

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task A_repeat_with_the_caller_key_returns_the_outcome_of_the_first_command_and_creates_no_other(string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"calls_key_repeat_{mode}",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        (Task<OutpostOutcome> first, OutpostCommandMessage command) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, mode, CallerKey, cancellationToken),
            cancellationToken);
        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded(Result), cancellationToken);
        AssertSucceeded(await first, command.Id);

        OutpostOutcome repeat = await TestCalls.CallAsync(server.Server, mode, CallerKey, cancellationToken);

        AssertSucceeded(repeat, command.Id);
        Assert.Single(await server.Schema.ListCommandsAsync(cancellationToken));
    }

    // The first is a Write that still runs. The repeat is a Read with its script and parameters; a Write whose script
    // differs only by a space at its end, since the script is compared exactly; and a Write with other parameters.
    [Theory]
    [InlineData("read", TestCalls.Script, TestCalls.Parameters)]
    [InlineData("write", TestCalls.Script + " ", TestCalls.Parameters)]
    [InlineData("write", TestCalls.Script, """{"n":2}""")]
    public async Task A_repeat_with_another_mode_script_or_parameters_is_refused_with_caller_key_conflict_and_creates_nothing(
        string mode,
        string script,
        string parameters)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"calls_key_conflict_{mode}_{script.Length}_{parameters.Length}",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        (Task<OutpostOutcome> first, OutpostCommandMessage command) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", CallerKey, cancellationToken),
            cancellationToken);

        OutpostOutcome conflict = await TestCalls.CallAsync(
            server.Server,
            mode,
            CallerKey,
            script,
            parameters,
            cancellationToken);

        Assert.Equal(OutpostOutcomeKindEnum.Refused, conflict.Kind);
        Assert.Equal(OutpostCodes.CallerKeyConflict, conflict.Code);
        Assert.Null(conflict.CommandId);
        Assert.Single(await server.Schema.ListCommandsAsync(cancellationToken));

        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded(Result), cancellationToken);
        AssertSucceeded(await first, command.Id);
    }

    // The repeat gives the members in another order, a number in another notation and in a nested object. It comes
    // while the first still runs, waits for its outcome and returns it.
    [Fact]
    public async Task A_repeat_whose_parameters_are_the_same_JSON_values_in_another_order_and_notation_returns_the_outcome_of_the_first()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "calls_key_same_json_values",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        (Task<OutpostOutcome> first, OutpostCommandMessage command) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(
                server.Server,
                "write",
                CallerKey,
                TestCalls.Script,
                """{"n":1,"lines":[{"item":"A","qty":2}]}""",
                cancellationToken),
            cancellationToken);

        Task<OutpostOutcome> repeat = TestCalls.CallAsync(
            server.Server,
            "write",
            CallerKey,
            TestCalls.Script,
            """{"lines":[{"qty":2.0,"item":"A"}],"n":1e0}""",
            cancellationToken);
        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded(Result), cancellationToken);

        AssertSucceeded(await first, command.Id);
        AssertSucceeded(await repeat, command.Id);
        Assert.Single(await server.Schema.ListCommandsAsync(cancellationToken));
    }

    // The agent's lease runs for the lease grace after the end of the hold, longer than the take timeout, so the first
    // command expires while the agent is connected and no poll is held. The next poll takes the repeat.
    [Fact]
    public async Task A_repeat_after_the_first_command_expired_creates_a_new_command()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TimeSpan takeTimeout = TimeSpan.FromSeconds(5);
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "calls_key_freed_by_expiry",
            options => options.TakeTimeout = takeTimeout,
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        await server.PollToTheEndOfItsHoldAsync(agent, [ReadAndWrite(TestCalls.BaseKey)], cancellationToken);
        Task<OutpostOutcome> first = TestCalls.CallAsync(server.Server, "write", CallerKey, cancellationToken);
        await server.WaitForTimerAsync(server.Clock.GetUtcNow() + takeTimeout, cancellationToken);
        server.Clock.Advance(takeTimeout);
        OutpostOutcome expired = await first;
        Assert.Equal(OutpostOutcomeKindEnum.Expired, expired.Kind);

        (Task<OutpostOutcome> repeat, OutpostCommandMessage command) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", CallerKey, cancellationToken),
            cancellationToken);
        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded(Result), cancellationToken);

        Assert.NotEqual(expired.CommandId, command.Id);
        AssertSucceeded(await repeat, command.Id);
        Assert.Equal(2, (await server.Schema.ListCommandsAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task A_repeat_after_the_agent_refused_the_first_command_creates_a_new_command()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "calls_key_freed_by_refusal",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        (Task<OutpostOutcome> first, OutpostCommandMessage refusedCommand) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", CallerKey, cancellationToken),
            cancellationToken);
        await agent.ConfirmOutcomeAsync(
            refusedCommand.Id,
            AgentOutcomes.Refused(OutpostOutcomeCodes.NotServed),
            cancellationToken);
        OutpostOutcome refused = await first;
        Assert.Equal(OutpostOutcomeKindEnum.Refused, refused.Kind);
        Assert.Equal(OutpostCodes.NotServed, refused.Code);
        Assert.Equal(refusedCommand.Id, refused.CommandId);

        (Task<OutpostOutcome> repeat, OutpostCommandMessage command) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", CallerKey, cancellationToken),
            cancellationToken);
        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded(Result), cancellationToken);

        Assert.NotEqual(refusedCommand.Id, command.Id);
        AssertSucceeded(await repeat, command.Id);
    }

    // The first command waits for a poll with a free worker when its agent is revoked, which ends it as Refused
    // `revoked` (rule 13) and frees the base key. The next agent that declares the base takes the repeat. Its poll has
    // a shorter hold than the poll of the revoked agent, which is still held.
    [Fact]
    public async Task A_repeat_after_a_revocation_refused_the_first_command_creates_a_new_command_for_the_next_holder_of_the_base()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "calls_key_freed_by_revocation",
            cancellationToken);
        using TestAgent revoked = await server.PairAgentAsync(cancellationToken);
        using TestAgent next = await server.PairAgentAsync(cancellationToken);
        OutpostPollBase busy = StoreCommands.Serving(TestCalls.BaseKey, 0, 0);
        await server.HoldPollAsync(revoked, revoked.NextPoll(server.Options.PollTimeout, [busy]), cancellationToken);
        Task<OutpostOutcome> first = await server.StartCallAsync(
            revoked.Id,
            () => TestCalls.CallAsync(server.Server, "write", CallerKey, cancellationToken),
            cancellationToken);
        Assert.True(await server.Server.RevokeAgentAsync(revoked.Id, cancellationToken));
        OutpostOutcome refused = await first;
        Assert.Equal(OutpostOutcomeKindEnum.Refused, refused.Kind);
        Assert.Equal(OutpostCodes.Revoked, refused.Code);

        (Task<OutpostOutcome> repeat, OutpostCommandMessage command) = await server.HandOutAsync(
            next,
            next.NextPoll(TimeSpan.FromSeconds(20), [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", CallerKey, cancellationToken),
            cancellationToken);
        await next.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded(Result), cancellationToken);

        Assert.Equal(next.Id, command.Agent);
        Assert.NotEqual(refused.CommandId, command.Id);
        AssertSucceeded(await repeat, command.Id);
    }

    // Both calls start before either is answered, so that both find no command for the key. The store creates one
    // command and the other call follows it; the poll, with one free worker, hands out that one.
    [Fact]
    public async Task Two_concurrent_first_calls_with_one_caller_key_create_one_command_and_both_return_its_outcome()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "calls_key_two_first_calls",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostPollRequest poll = agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]);
        Task<AgentAnswer> held = await server.HoldPollAsync(agent, poll, cancellationToken);

        Task<OutpostOutcome> one = TestCalls.CallAsync(server.Server, "write", CallerKey, cancellationToken);
        Task<OutpostOutcome> other = TestCalls.CallAsync(server.Server, "write", CallerKey, cancellationToken);

        OutpostCommandMessage command = Assert.Single(agent.ReadCommands(agent.ReadPollAnswer(poll, await held)));
        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded(Result), cancellationToken);

        AssertSucceeded(await one, command.Id);
        AssertSucceeded(await other, command.Id);
        CommandRow row = Assert.Single(await server.Schema.ListCommandsAsync(cancellationToken));
        Assert.Equal(command.Id, row.Id);
    }

    private static void AssertSucceeded(OutpostOutcome outcome, Guid commandId)
    {
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(commandId, outcome.CommandId);
        Assert.Equal(Result, outcome.Result!.Value.GetRawText());
    }
}
