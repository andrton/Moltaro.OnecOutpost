using System.Net;
using System.Security.Cryptography;
using System.Text;
using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The line Server of check (the S10b brief, "The server and the skew", and §6.3, §9 and §12 of docs/protocol.md): one
// outcome of a made-up command, refused as not-addressed, signed with the agent's key of secrets.dat and sent to the
// server of agent.json through the proxy of the policy, waiting the policy's requestTimeout, as the agent sends its
// outcomes; never a poll, which would replace the poll that the running service holds. It passes for a verified 200
// that confirms the command alone. Any other answer is named with its status when one came and the console's own
// reason, ServerAnswerSentences', never with text of a body that it has not verified: each body of the stub that could
// be shown carries a mark, which the output never holds.
public sealed class ServerLineTests : IDisposable
{
    private const string Mark = "text-of-an-unverified-body";

    private readonly CheckedMachine _checked = new();

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    [Fact]
    public async Task Check_sends_the_server_one_outcome_of_a_made_up_command_refused_as_not_addressed_and_signed_by_the_agent()
    {
        OutpostOutcomeRequest? sent = null;

        CheckRun run = await _checked.RunAsync(exchange =>
        {
            // Read as the server reads it, verified with the agent's key.
            Assert.Equal(CheckedMachine.Agent, exchange.Agent);
            sent = exchange.ReadOutcome();
            exchange.Confirm();
        });

        Assert.NotNull(sent);
        Assert.NotEqual(Guid.Empty, sent.Command);
        Assert.NotEqual(CheckedMachine.Agent, sent.Command);
        Assert.Equal(
            OwnOutcome.Write(OutpostAgentOutcomeKindEnum.Refused, OutpostOutcomeCodes.NotAddressed),
            sent.Outcome.ToArray());
        Assert.Equal([(CheckedMachine.Server, (Uri?)new Uri("http://proxy.example.com:3128"))], Machine.Routes);
        run.Passed(CheckLineEnum.Server);
    }

    // The policy's requestTimeout, 20 seconds, on the fake clock.
    [Fact]
    public async Task A_server_that_does_not_answer_within_the_request_timeout_of_the_policy_fails_the_line_as_unreachable()
    {
        Task<CommandAppResult> started = _checked.Start();
        await _checked.Stub.NextOutcomeAsync();
        await TestTimes.PassExactlyAsync(Machine.Clock, TimeSpan.FromSeconds(20), "outcome of check");
        CheckRun run = CheckedMachine.Ended(await TestTimes.EndOf(started));

        run.AssertCheckFailed();
        Assert.Equal(
            ServerAnswerSentences.Of(OutpostRequestKindEnum.Outcome, ServerAnswerCauseEnum.Unreachable, null, null),
            run.Failed(CheckLineEnum.Server));
        Assert.Equal("not checked: no verified answer came", run.Failed(CheckLineEnum.ClockSkew));
    }

    // Each row of the table of an outcome, as the agent's connection judges the answer (§9), its status when one came;
    // ServerAnswerSentencesTests states the words of each.
    [Theory]
    [InlineData("a failed connection", null, "Unreachable", null)]
    [InlineData("the unsigned answer of a version not spoken", 400, "ProtocolVersion", null)]
    [InlineData("a 200 signed by another key", 200, "Signature", null)]
    [InlineData("a refusal of the agent's signature", 401, "Refusal", "signature")]
    [InlineData("a refusal of the form", 400, "Refusal", "form")]
    [InlineData("a refusal of the size", 413, "Refusal", "result-too-large")]
    [InlineData("a bare 401 with a page", 401, "Bare", null)]
    [InlineData("a bare 403 with a page", 403, "Bare", null)]
    [InlineData("a 413 of a hop with a page", 413, "Bare", null)]
    [InlineData("a 500 with a page", 500, "Answer", null)]
    public async Task An_answer_that_check_does_not_accept_fails_the_line_with_its_status_and_the_console_own_reason_never_the_body(
        string answer,
        int? status,
        string cause,
        string? code)
    {
        CheckRun run = await _checked.RunAsync(exchange => Give(exchange, answer));

        run.AssertCheckFailed();
        Assert.Equal(
            ServerAnswerSentences.Of(OutpostRequestKindEnum.Outcome, Enum.Parse<ServerAnswerCauseEnum>(cause), status, code),
            run.Failed(CheckLineEnum.Server));
        Assert.DoesNotContain(Mark, run.Output, StringComparison.Ordinal);
    }

    private static void Give(ServerExchange exchange, string answer)
    {
        switch (answer)
        {
            case "a failed connection":
                exchange.Fail(new HttpRequestException($"The connection was reset: {Mark}"));
                break;
            case "the unsigned answer of a version not spoken":
                exchange.AnswerUnsupportedVersion();
                break;
            case "a 200 signed by another key":
                // A hop that opens TLS and answers with a key of its own.
                using (ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    exchange.AnswerSignedBy(other, 200, Encoding.UTF8.GetBytes($$"""{"confirmed":"{{Mark}}"}"""));
                }

                break;
            case "a refusal of the agent's signature":
                exchange.Refuse(401, OutpostProtocolCodes.Signature);
                break;
            case "a refusal of the form":
                exchange.Refuse(400, OutpostProtocolCodes.Form);
                break;
            case "a refusal of the size":
                exchange.Refuse(413, OutpostProtocolCodes.ResultTooLarge);
                break;
            case "a bare 401 with a page":
                exchange.Answer(Page(HttpStatusCode.Unauthorized));
                break;
            case "a bare 403 with a page":
                exchange.Answer(Page(HttpStatusCode.Forbidden));
                break;
            case "a 413 of a hop with a page":
                exchange.Answer(Page(HttpStatusCode.RequestEntityTooLarge));
                break;
            case "a 500 with a page":
                exchange.Answer(Page(HttpStatusCode.InternalServerError));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(answer), answer, "No such answer.");
        }
    }

    // A page of a hop on the path, without an Outpost-Signature.
    private static HttpResponseMessage Page(HttpStatusCode status)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent($"<html><body>{Mark}</body></html>", Encoding.UTF8, "text/html"),
        };
    }
}
