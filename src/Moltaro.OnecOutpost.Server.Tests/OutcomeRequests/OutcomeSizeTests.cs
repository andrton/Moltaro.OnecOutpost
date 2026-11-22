using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.Server.Tests.Loopback;
using Moltaro.OnecOutpost.Server.Tests.Migrations;
using Moltaro.OnecOutpost.Server.Tests.Store;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.OutcomeRequests;

// The size of an outcome over loopback HTTP (concept rules 5 and 11, §6.3 and §9 of docs/protocol.md): an outcome
// object larger than the outcome limit of its command, or than OutcomeSizeLimit for a command the server does not
// know, gets the signed 413 `result-too-large` and changes nothing; one at the limit is confirmed. The size is the
// length of the UTF-8 text of the object as it came. The limits are the least the option allows, or twice that, so
// that each body is within the body limit of the endpoint.
[Trait("Requires", "PostgreSQL")]
public sealed class OutcomeSizeTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const int Limit = OutpostProtocol.MinOutcomeSizeLimit;

    // The agent then replaces its outcome with Unknown `result-too-large` (rule 11), which the server keeps and the
    // Write returns: the server never makes a Write Failed after the agent reported it.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task An_outcome_beyond_the_limit_of_its_command_gets_the_signed_413_and_its_Write_ends_as_the_Unknown_the_agent_sends_then()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "outcomes_size_beyond",
            options => options.OutcomeSizeLimit = Limit,
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        (Task<OutpostOutcome> write, OutpostCommandMessage command) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        Assert.Equal(Limit, command.OutcomeLimit);
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        AgentAnswer refused = await agent.SendOutcomeAsync(command.Id, OutcomeOfSize(Limit + 1), cancellationToken);

        Assert.Equal(413, refused.Status);
        Assert.Equal(OutpostProtocolCodes.ResultTooLarge, agent.ReadRefusal(refused).Code);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);

        await agent.ConfirmOutcomeAsync(
            command.Id,
            AgentOutcomes.Unknown(OutpostOutcomeCodes.ResultTooLarge),
            cancellationToken);
        OutpostOutcome outcome = await write;

        Assert.Equal(OutpostOutcomeKindEnum.Unknown, outcome.Kind);
        Assert.Equal(OutpostCodes.ResultTooLarge, outcome.Code);
        Assert.Equal(command.Id, outcome.CommandId);
    }

    // The command was made by a node whose OutcomeSizeLimit was lower than this node's, so the limit it carries is not
    // the option: the outcome is measured against the limit of its command. It is taken, as a poll of the agent would
    // have handed it out.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task An_outcome_is_measured_against_the_limit_that_its_command_carries(int beyond)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"outcomes_size_of_its_command_{beyond}",
            options => options.OutcomeSizeLimit = 2 * Limit,
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        CommandRow command = StoreCommands.Create(
            agent.Id,
            TestCalls.BaseKey,
            OutpostModeEnum.Read,
            null,
            server.Clock.GetUtcNow(),
            outcomeLimit: Limit);
        command.Phase = CommandPhaseEnum.HandedOut;
        command.RunLimit = StoreCommands.ReadRunLimit;
        await server.Schema.InsertCommandsAsync([command], cancellationToken);
        byte[] outcome = OutcomeOfSize(Limit + beyond);

        AgentAnswer answer = await agent.SendOutcomeAsync(command.Id, outcome, cancellationToken);
        CommandRow? row = await server.Schema.FindCommandAsync(command.Id, cancellationToken);

        if (beyond == 0)
        {
            agent.ReadOutcomeAnswer(command.Id, answer);
            Assert.Equal(OutpostOutcomeKindEnum.Succeeded, row?.OutcomeKind);
            Assert.Equal(outcome, row?.Outcome);
        }
        else
        {
            Assert.Equal(413, answer.Status);
            Assert.Equal(OutpostProtocolCodes.ResultTooLarge, agent.ReadRefusal(answer).Code);
            Assert.Null(row?.OutcomeKind);
        }
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task An_outcome_for_a_command_that_the_server_does_not_know_is_measured_against_OutcomeSizeLimit(int beyond)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"outcomes_size_of_unknown_{beyond}",
            options => options.OutcomeSizeLimit = Limit,
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        Guid madeUp = Guid.NewGuid();
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        AgentAnswer answer = await agent.SendOutcomeAsync(madeUp, OutcomeOfSize(Limit + beyond), cancellationToken);

        if (beyond == 0)
        {
            agent.ReadOutcomeAnswer(madeUp, answer);
        }
        else
        {
            Assert.Equal(413, answer.Status);
            Assert.Equal(OutpostProtocolCodes.ResultTooLarge, agent.ReadRefusal(answer).Code);
        }

        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // A Succeeded outcome object whose UTF-8 text is exactly `size` bytes: its result is a string of letters, which the
    // writer of §2 writes as they are.
    private static byte[] OutcomeOfSize(int size)
    {
        int envelope = OutpostCodec.Write(AgentOutcomes.Succeeded("\"\"")).Length;
        byte[] outcome = OutpostCodec.Write(AgentOutcomes.Succeeded("\"" + new string('x', size - envelope) + "\""));

        Assert.Equal(size, outcome.Length);

        return outcome;
    }
}
