using System.Text;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Tests.Loopback;
using Moltaro.OnecOutpost.Server.Tests.Migrations;
using Moltaro.OnecOutpost.Server.Tests.Store;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.OutcomeRequests;

// The `outcome` request over loopback HTTP (§6.3 of docs/protocol.md, concept rule 11): the server checks the
// signature, the body by §2 and the members of the outcome object by §8 for the mode of its command, and answers a
// failure with the signed refusal of §9, bound to the body it received; it then confirms the outcome with a signed 200,
// and keeps only the first outcome of a command from the agent it was handed to. Whether an outcome was kept shows in
// the store and in what the call or a repeat with its caller key returns.
[Trait("Requires", "PostgreSQL")]
public sealed class OutcomeRequestTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string CallerKey = "invoice-17";

    // Another paired agent sends an outcome for the command first, then the agent it was handed to sends two.
    [Fact]
    public async Task Only_the_first_outcome_of_a_command_from_the_agent_it_was_handed_to_is_kept_and_every_one_is_confirmed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "outcomes_first_from_its_agent",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        using TestAgent other = await server.PairAgentAsync(cancellationToken);
        (Task<OutpostOutcome> write, OutpostCommandMessage command) = await HandOutWriteAsync(
            server,
            agent,
            cancellationToken);
        SchemaSnapshot handedOut = await server.Schema.TakeSnapshotAsync(cancellationToken);

        await other.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("""{"forged":true}"""), cancellationToken);
        Assert.Equal(handedOut.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);

        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("""{"rows":3}"""), cancellationToken);
        OutpostOutcome first = await write;
        SchemaSnapshot kept = await server.Schema.TakeSnapshotAsync(cancellationToken);

        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.ScriptError("A later error"), cancellationToken);
        OutpostOutcome repeat = await TestCalls.CallAsync(server.Server, "write", CallerKey, cancellationToken);

        OutpostOutcome[] returned = [first, repeat];

        Assert.Equal(kept.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
        Assert.All(
            returned,
            outcome =>
            {
                Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
                Assert.Equal(command.Id, outcome.CommandId);
                Assert.Equal("""{"rows":3}""", outcome.Result!.Value.GetRawText());
            });
    }

    // For a command the server does not know, either mode will do (§6.3): an outcome only a Write may have, one only a
    // Read may have, and the one that the console's check sends (§12).
    [Theory]
    [InlineData("write-only")]
    [InlineData("read-only")]
    [InlineData("console")]
    public async Task An_outcome_for_a_command_that_the_server_does_not_know_is_confirmed_and_nothing_is_kept(
        string outcomeCase)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"outcomes_unknown_command_{outcomeCase.Replace('-', '_')}",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostOutcomeMessage outcome = outcomeCase switch
        {
            "write-only" => AgentOutcomes.Unknown(OutpostOutcomeCodes.TransactionEnded),
            "read-only" => AgentOutcomes.Failed(OutpostOutcomeCodes.RunLimit),
            _ => AgentOutcomes.Refused(OutpostOutcomeCodes.NotAddressed),
        };
        Guid madeUp = Guid.NewGuid();
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        OutpostOutcomeAnswer confirmed = await agent.ConfirmOutcomeAsync(madeUp, outcome, cancellationToken);

        Assert.Equal(madeUp, confirmed.Confirmed);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // §8: an outcome that only the other mode allows, and then one that the mode of the command allows, which the call
    // returns.
    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task An_outcome_that_the_mode_of_its_command_does_not_allow_gets_the_signed_form_refusal_and_changes_nothing(
        string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"outcomes_not_for_the_mode_{mode}",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        (Task<OutpostOutcome> call, OutpostCommandMessage command) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, mode, CallerKey, cancellationToken),
            cancellationToken);
        bool write = mode == "write";
        OutpostOutcomeMessage otherMode = write
            ? AgentOutcomes.Failed(OutpostOutcomeCodes.RunLimit)
            : AgentOutcomes.Unknown(OutpostOutcomeCodes.TransactionEnded);
        OutpostOutcomeMessage thisMode = write
            ? AgentOutcomes.Unknown(OutpostOutcomeCodes.RunLimit)
            : AgentOutcomes.Failed(OutpostOutcomeCodes.RunLimit);
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        AgentAnswer refused = await agent.SendOutcomeAsync(command.Id, otherMode, cancellationToken);

        Assert.Equal(400, refused.Status);
        Assert.Equal(OutpostProtocolCodes.Form, agent.ReadRefusal(refused).Code);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);

        await agent.ConfirmOutcomeAsync(command.Id, thisMode, cancellationToken);
        OutpostOutcome outcome = await call;

        Assert.Equal(write ? OutpostOutcomeKindEnum.Unknown : OutpostOutcomeKindEnum.Failed, outcome.Kind);
        Assert.Equal(OutpostCodes.RunLimit, outcome.Code);
    }

    // The agent signed one outcome, and another body arrives with that signature: the refusal answers the body that
    // came.
    [Fact]
    public async Task An_outcome_whose_body_changed_after_it_was_signed_gets_the_signed_signature_refusal_and_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "outcomes_changed_body",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        (_, OutpostCommandMessage command) = await HandOutWriteAsync(server, agent, cancellationToken);
        byte[] signed = OutcomeBody(command.Id, AgentOutcomes.Succeeded("""{"rows":3}"""));
        byte[] changed = OutcomeBody(command.Id, AgentOutcomes.ScriptError("A forged error"));
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        AgentAnswer answer = await agent.SendAsync(
            OutpostRequestKindEnum.Outcome,
            changed,
            agent.Sign(OutpostRequestKindEnum.Outcome, signed),
            cancellationToken);

        Assert.Equal(401, answer.Status);
        Assert.Equal(OutpostProtocolCodes.Signature, agent.ReadRefusal(answer).Code);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // The outcome object gives `kind` twice, which §2 refuses, and is larger than the outcome size limit, which is the
    // least the option allows; the body is within its own limit. The body is checked first, whatever the size of the
    // outcome (§6.3).
    [Fact]
    public async Task An_outcome_whose_body_breaks_the_rules_of_its_message_gets_the_signed_form_refusal_whatever_its_size()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "outcomes_broken_body",
            options => options.OutcomeSizeLimit = OutpostProtocol.MinOutcomeSizeLimit,
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        (_, OutpostCommandMessage command) = await HandOutWriteAsync(server, agent, cancellationToken);
        string result = new('x', OutpostProtocol.MinOutcomeSizeLimit);
        byte[] body = Encoding.UTF8.GetBytes(
            $$$"""{"command":"{{{command.Id:D}}}","outcome":{"kind":"succeeded","kind":"succeeded","code":null,"result":"{{{result}}}","errorText":null,"messages":[],"messagesTrimmed":false}}""");
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        AgentAnswer answer = await agent.SendAsync(OutpostRequestKindEnum.Outcome, body, cancellationToken);

        Assert.Equal(400, answer.Status);
        Assert.Equal(OutpostProtocolCodes.Form, agent.ReadRefusal(answer).Code);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    private static Task<(Task<OutpostOutcome> Call, OutpostCommandMessage Command)> HandOutWriteAsync(
        LoopbackServer server,
        TestAgent agent,
        CancellationToken cancellationToken)
    {
        return server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", CallerKey, cancellationToken),
            cancellationToken);
    }

    private static byte[] OutcomeBody(Guid commandId, OutpostOutcomeMessage outcome)
    {
        return OutpostCodec.Write(new OutpostOutcomeRequest(commandId, OutpostCodec.Write(outcome)));
    }
}
