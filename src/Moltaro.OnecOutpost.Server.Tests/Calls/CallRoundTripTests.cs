using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using static Moltaro.OnecOutpost.TestSupport.Loopback.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Calls;

// Read and Write end to end over loopback HTTP, with a test agent: a call creates a signed
// command and wakes the held poll of its agent, which hands the command out; the agent sends its outcome, the server
// confirms it, and the call returns it. The clock does not move, so the poll answers because the call woke it. The
// options are not the defaults, so that the command shows that it carries the values of the node that made it (§7 of
// docs/protocol.md), and the clock stands at a time finer than a millisecond, which the command truncates.
[Trait("Requires", "PostgreSQL")]
public sealed class CallRoundTripTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string Script = "Result = Parameters.Get(\"period\");";

    private const string Parameters = """{"period":"2026-11","items":[1,2.5,true,null]}""";

    private const int OutcomeSizeLimit = 1024 * 1024;

    private static readonly TimeSpan TakeTimeout = TimeSpan.FromSeconds(45);

    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task A_read_reaches_the_held_poll_of_its_agent_as_a_signed_command_and_returns_the_outcome_that_the_agent_sends()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync("calls_round_trip_of_a_read", cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostPollRequest poll = agent.NextPoll(server.Options.PollTimeout, [ReadOnly(TestCalls.BaseKey)]);
        Task<AgentAnswer> held = await server.HoldPollAsync(agent, poll, cancellationToken);
        DateTimeOffset issued = ToMilliseconds(server.Clock.GetUtcNow());
        OutpostReadRequest request = new()
        {
            BaseKey = TestCalls.BaseKey,
            Script = Script,
            Parameters = JsonNode.Parse(Parameters)!.AsObject(),
        };

        Task<OutpostOutcome> read = TestCalls.WithDeadline(server.Server.ReadAsync(request, cancellationToken));

        OutpostPollAnswer answer = agent.ReadPollAnswer(poll, await held);
        OutpostCommandMessage command = Assert.Single(agent.ReadCommands(answer));
        AssertCommand(command, agent.Id, OutpostModeEnum.Read, null, issued);
        await AssertStoredAsHandedOutAsync(server, answer, command, cancellationToken);

        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("""{"rows":3}"""), cancellationToken);
        OutpostOutcome outcome = await read;

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(command.Id, outcome.CommandId);
        Assert.Equal("""{"rows":3}""", outcome.Result!.Value.GetRawText());
        Assert.Equal<string>(["Posted"], outcome.Messages);
        Assert.False(outcome.MessagesTrimmed);
        Assert.Null(outcome.Code);
        Assert.Null(outcome.ErrorText);
    }

    [Fact]
    public async Task A_write_reaches_the_held_poll_of_its_agent_as_a_signed_command_and_returns_the_outcome_that_the_agent_sends()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync("calls_round_trip_of_a_write", cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostPollRequest poll = agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]);
        Task<AgentAnswer> held = await server.HoldPollAsync(agent, poll, cancellationToken);
        DateTimeOffset issued = ToMilliseconds(server.Clock.GetUtcNow());
        OutpostWriteRequest request = new()
        {
            BaseKey = TestCalls.BaseKey,
            CallerKey = "invoice-2026-11-17",
            Script = Script,
            Parameters = JsonNode.Parse(Parameters)!.AsObject(),
        };

        Task<OutpostOutcome> write = TestCalls.WithDeadline(server.Server.WriteAsync(request, cancellationToken));

        OutpostPollAnswer answer = agent.ReadPollAnswer(poll, await held);
        OutpostCommandMessage command = Assert.Single(agent.ReadCommands(answer));
        AssertCommand(command, agent.Id, OutpostModeEnum.Write, "invoice-2026-11-17", issued);
        await AssertStoredAsHandedOutAsync(server, answer, command, cancellationToken);

        await agent.ConfirmOutcomeAsync(
            command.Id,
            AgentOutcomes.ScriptError("Failed to call the context method (Write)"),
            cancellationToken);
        OutpostOutcome outcome = await write;

        Assert.Equal(OutpostOutcomeKindEnum.Failed, outcome.Kind);
        Assert.Equal(command.Id, outcome.CommandId);
        Assert.Equal(OutpostCodes.ScriptError, outcome.Code);
        Assert.Equal("Failed to call the context method (Write)", outcome.ErrorText);
        Assert.Equal<string>(["The field is not filled in"], outcome.Messages);
        Assert.False(outcome.MessagesTrimmed);
        Assert.Null(outcome.Result);
    }

    // The members of §7, each as the call and the options of the node give it: issued at the node's time truncated to
    // the millisecond, with the take deadline TakeTimeout later, the skew and the outcome size limit of the node.
    private static void AssertCommand(
        OutpostCommandMessage command,
        Guid agentId,
        OutpostModeEnum mode,
        string? callerKey,
        DateTimeOffset issued)
    {
        Assert.Equal(OutpostProtocol.Version, command.Protocol);
        Assert.Equal(agentId, command.Agent);
        Assert.Equal(TestCalls.BaseKey, command.Base);
        Assert.Equal(mode, command.Mode);
        Assert.Equal(callerKey, command.CallerKey);
        Assert.Equal(Script, command.Script);
        Assert.Equal(Parameters, command.Parameters.GetRawText());
        Assert.Equal(issued, command.Issued);
        Assert.Equal(issued + TakeTimeout, command.Deadline);
        Assert.Equal(ClockSkew, command.Skew);
        Assert.Equal(OutcomeSizeLimit, command.OutcomeLimit);
    }

    // The command is signed once: the store keeps the bytes and the signature that the poll handed out, with the
    // deadline that was signed, and the command is HandedOut.
    private static async Task AssertStoredAsHandedOutAsync(
        LoopbackServer server,
        OutpostPollAnswer answer,
        OutpostCommandMessage command,
        CancellationToken cancellationToken)
    {
        OutpostSignedCommand handed = Assert.Single(answer.Commands);
        CommandRow? row = await server.Schema.FindCommandAsync(command.Id, cancellationToken);

        Assert.NotNull(row);
        Assert.Equal(handed.Bytes, row.Bytes);
        Assert.Equal(handed.Signature, row.Signature);
        Assert.Equal(command.Deadline, row.Deadline);
        Assert.Equal(CommandPhaseEnum.HandedOut, row.Phase);
        Assert.Null(row.OutcomeKind);
    }

    private static DateTimeOffset ToMilliseconds(DateTimeOffset time)
    {
        return time.AddTicks(-(time.Ticks % TimeSpan.TicksPerMillisecond));
    }

    // The clock moves to a time finer than a millisecond before any poll, which no timer of the node is due by.
    private async Task<LoopbackServer> StartAsync(string schemaName, CancellationToken cancellationToken)
    {
        LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            schemaName,
            options =>
            {
                options.TakeTimeout = TakeTimeout;
                options.ClockSkew = ClockSkew;
                options.OutcomeSizeLimit = OutcomeSizeLimit;
            },
            cancellationToken);

        server.Clock.Advance(TimeSpan.FromTicks(12_345_678));

        return server;
    }
}
