using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using static Moltaro.OnecOutpost.TestSupport.Loopback.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Calls;

// Concept rule 4 and Q15: a repeat that found its command before the purge, but passes it after the purge, makes one
// more attempt at admission. The order is fixed by the subscription gate, the purge timer and a verified agent poll.
[Trait("Requires", "PostgreSQL")]
public sealed class CallerKeyPurgeTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string CallerKey = "invoice-17";

    private static readonly TimeSpan Retention = TimeSpan.FromHours(1);

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task A_repeat_that_loses_its_command_to_the_purge_can_create_a_fresh_command(string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CommandSubscriptionGate gate = new();
        await using LoopbackServer server = await StartAsync($"calls_key_purged_{mode}", gate, cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostCommandMessage ended = await CompleteCallAsync(server, agent, mode, TestCalls.Script, "1", cancellationToken);

        using HeldCommandSubscription original = gate.HoldNext(key => key == server.SignalKey(ended.Id), cancellationToken);
        Task<OutpostOutcome> repeat = StartCall(server, mode, cancellationToken);
        await original.WaitAsync(cancellationToken);
        await PurgeAsync(server, ended.Id, cancellationToken);

        (Task<OutpostOutcome> _, OutpostCommandMessage replacement) = await server.HandOutAsync(
            agent,
            NextPoll(server, agent),
            () =>
            {
                original.Release();
                return repeat;
            },
            cancellationToken);
        await agent.ConfirmOutcomeAsync(replacement.Id, AgentOutcomes.Succeeded("2"), cancellationToken);

        Assert.NotEqual(ended.Id, replacement.Id);
        Assert.Equal(ended.Mode, replacement.Mode);
        Assert.Equal(CallerKey, replacement.CallerKey);
        Assert.Equal(TestCalls.Script, replacement.Script);
        Assert.Equal(TestCalls.Parameters, replacement.Parameters.GetRawText());
        Assert.Equal(server.Clock.GetUtcNow(), replacement.Issued);
        Assert.Equal(replacement.Issued + server.Options.TakeTimeout, replacement.Deadline);
        Assert.True(replacement.Deadline > ended.Deadline);
        AssertSucceeded(await repeat, replacement.Id, "2");
        Assert.Equal(replacement.Id, Assert.Single(await server.Schema.ListCommandsAsync(cancellationToken)).Id);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task A_repeat_that_loses_its_command_to_the_purge_is_unavailable_if_its_agent_stayed_away(string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CommandSubscriptionGate gate = new();
        await using LoopbackServer server = await StartAsync($"calls_key_purged_away_{mode}", gate, cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostCommandMessage ended = await CompleteCallAsync(server, agent, mode, TestCalls.Script, "1", cancellationToken);

        using HeldCommandSubscription original = gate.HoldNext(key => key == server.SignalKey(ended.Id), cancellationToken);
        Task<OutpostOutcome> repeat = StartCall(server, mode, cancellationToken);
        await original.WaitAsync(cancellationToken);
        await PurgeAsync(server, ended.Id, cancellationToken);
        original.Release();

        OutpostOutcome outcome = await repeat;
        Assert.Equal(OutpostOutcomeKindEnum.Unavailable, outcome.Kind);
        Assert.Null(outcome.CommandId);
        Assert.Empty(await server.Schema.ListCommandsAsync(cancellationToken));
    }

    [Fact]
    public async Task A_repeat_that_loses_its_command_to_the_purge_obeys_the_current_mode()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CommandSubscriptionGate gate = new();
        await using LoopbackServer server = await StartAsync("calls_key_purged_mode_closed", gate, cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostCommandMessage ended = await CompleteCallAsync(server, agent, "write", TestCalls.Script, "1", cancellationToken);

        using HeldCommandSubscription original = gate.HoldNext(key => key == server.SignalKey(ended.Id), cancellationToken);
        Task<OutpostOutcome> repeat = StartCall(server, "write", cancellationToken);
        await original.WaitAsync(cancellationToken);
        await PurgeAsync(server, ended.Id, cancellationToken);
        await server.PollToTheEndOfItsHoldAsync(agent, [ReadOnly(TestCalls.BaseKey)], cancellationToken);
        original.Release();

        OutpostOutcome outcome = await repeat;
        Assert.Equal(OutpostOutcomeKindEnum.Refused, outcome.Kind);
        Assert.Equal(OutpostCodes.ModeClosed, outcome.Code);
        Assert.Null(outcome.CommandId);
        Assert.Empty(await server.Schema.ListCommandsAsync(cancellationToken));
    }

    [Theory]
    [InlineData("identical", TestCalls.Script)]
    [InlineData("different", "Result = 2;")]
    public async Task A_repeat_checks_the_replacement_holding_its_key_before_the_base_and_agent(string match, string script)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CommandSubscriptionGate gate = new();
        await using LoopbackServer server = await StartAsync($"calls_key_purged_replaced_{match}", gate, cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostCommandMessage ended = await CompleteCallAsync(server, agent, "write", TestCalls.Script, "1", cancellationToken);

        using HeldCommandSubscription original = gate.HoldNext(key => key == server.SignalKey(ended.Id), cancellationToken);
        Task<OutpostOutcome> repeat = StartCall(server, "write", cancellationToken);
        await original.WaitAsync(cancellationToken);
        await PurgeAsync(server, ended.Id, cancellationToken);
        OutpostCommandMessage replacement = await CompleteCallAsync(server, agent, "write", script, "2", cancellationToken);
        Assert.True(await server.Server.RevokeAgentAsync(agent.Id, cancellationToken));
        original.Release();

        OutpostOutcome outcome = await repeat;

        if (match == "identical")
        {
            AssertSucceeded(outcome, replacement.Id, "2");
        }
        else
        {
            Assert.Equal(OutpostOutcomeKindEnum.Refused, outcome.Kind);
            Assert.Equal(OutpostCodes.CallerKeyConflict, outcome.Code);
            Assert.Null(outcome.CommandId);
        }

        Assert.Equal(replacement.Id, Assert.Single(await server.Schema.ListCommandsAsync(cancellationToken)).Id);
    }

    [Fact]
    public async Task A_call_faults_if_the_command_it_created_vanishes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CommandSubscriptionGate gate = new();
        await using LoopbackServer server = await StartAsync("calls_key_purged_own", gate, cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        using HeldCommandSubscription own = gate.HoldNext(key => key != server.SignalKey(agent.Id), cancellationToken);
        (Task<OutpostOutcome> call, OutpostCommandMessage command) = await server.HandOutAsync(
            agent, NextPoll(server, agent), () => StartCall(server, "write", cancellationToken), cancellationToken);
        Assert.Equal(server.SignalKey(command.Id), await own.WaitAsync(cancellationToken));

        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("1"), cancellationToken);
        await PurgeAsync(server, command.Id, cancellationToken);
        own.Release();

        await Assert.ThrowsAsync<InvalidOperationException>(() => call);
        Assert.Empty(await server.Schema.ListCommandsAsync(cancellationToken));
    }

    [Fact]
    public async Task A_repeat_whose_replacement_also_vanishes_faults_after_one_readmission()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using CommandSubscriptionGate gate = new();
        await using LoopbackServer server = await StartAsync("calls_key_purged_twice", gate, cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostCommandMessage ended = await CompleteCallAsync(server, agent, "write", TestCalls.Script, "1", cancellationToken);

        using HeldCommandSubscription original = gate.HoldNext(key => key == server.SignalKey(ended.Id), cancellationToken);
        Task<OutpostOutcome> repeat = StartCall(server, "write", cancellationToken);
        await original.WaitAsync(cancellationToken);
        await PurgeAsync(server, ended.Id, cancellationToken);

        using HeldCommandSubscription next = gate.HoldNext(key => key != server.SignalKey(agent.Id), cancellationToken);
        (Task<OutpostOutcome> _, OutpostCommandMessage replacement) = await server.HandOutAsync(
            agent,
            NextPoll(server, agent),
            () =>
            {
                original.Release();
                return repeat;
            },
            cancellationToken);
        Assert.Equal(server.SignalKey(replacement.Id), await next.WaitAsync(cancellationToken));
        await agent.ConfirmOutcomeAsync(replacement.Id, AgentOutcomes.Succeeded("2"), cancellationToken);
        await PurgeAsync(server, replacement.Id, cancellationToken);
        next.Release();

        await Assert.ThrowsAsync<InvalidOperationException>(() => repeat);
        Assert.Empty(await server.Schema.ListCommandsAsync(cancellationToken));
    }

    private Task<LoopbackServer> StartAsync(
        string schema,
        CommandSubscriptionGate gate,
        CancellationToken cancellationToken)
    {
        return LoopbackServer.StartAsync(
            postgres,
            schema,
            options =>
            {
                options.OutcomeRetention = Retention;
                options.PurgeInterval = Retention;
            },
            gate,
            cancellationToken);
    }

    // No poll or call timer remains while the subscription is held. The next purge timer proves the round ended.
    private static async Task PurgeAsync(LoopbackServer server, Guid commandId, CancellationToken cancellationToken)
    {
        await server.WaitForTimerAsync(server.Clock.GetUtcNow() + Retention, cancellationToken);
        server.Clock.Advance(Retention);
        await server.WaitForTimerAsync(server.Clock.GetUtcNow() + Retention, cancellationToken);
        Assert.Null(await server.Schema.FindCommandAsync(commandId, cancellationToken));
    }

    private static OutpostPollRequest NextPoll(LoopbackServer server, TestAgent agent)
    {
        return agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]);
    }

    private static Task<OutpostOutcome> StartCall(LoopbackServer server, string mode, CancellationToken cancellationToken)
    {
        return Task.Run(() => TestCalls.CallAsync(server.Server, mode, CallerKey, cancellationToken), cancellationToken);
    }

    private static async Task<OutpostCommandMessage> CompleteCallAsync(
        LoopbackServer server,
        TestAgent agent,
        string mode,
        string script,
        string result,
        CancellationToken cancellationToken)
    {
        (Task<OutpostOutcome> call, OutpostCommandMessage command) = await server.HandOutAsync(
            agent,
            NextPoll(server, agent),
            () => TestCalls.CallAsync(server.Server, mode, CallerKey, script, TestCalls.Parameters, cancellationToken),
            cancellationToken);
        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded(result), cancellationToken);
        AssertSucceeded(await call, command.Id, result);

        return command;
    }

    private static void AssertSucceeded(OutpostOutcome outcome, Guid commandId, string result)
    {
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(commandId, outcome.CommandId);
        Assert.Equal(result, outcome.Result!.Value.GetRawText());
    }
}
