using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;
using static Moltaro.OnecOutpost.Agent.Tests.Transport.TestConnection;

namespace Moltaro.OnecOutpost.Agent.Tests.Transport;

// What the agent makes of an answer to an outcome request (§6.3, §9 and §10 of docs/protocol.md, the S6 brief,
// "AgentConnection"): Confirmed for a verified 200 that confirms the request's command; TooLarge for a verified
// result-too-large and for a 413 without an Outpost-Signature that verifies, whatever its body, as anything on the path
// sends one; Lost for every other answer, and for none. The agent waits requestTimeout over the answer and its body,
// and reads no more than 64 KiB. The answer is judged once into an OutcomeAnswer (the S10b brief, "The answer to an
// outcome"), which the console's check reads too: the time of a verified confirmation or refusal, the status when one
// came, and for any answer but a confirmation its cause in §9's order, with the code of a verified refusal.
public sealed class OutcomeAnswerTests
{
    [Fact]
    public async Task A_verified_200_that_confirms_the_command_is_Confirmed()
    {
        using TestConnection test = new();

        Task<OutcomeResultEnum> result = test.SendOutcomeAsync(Guid.NewGuid(), TestOutcomes.Succeeded());
        (await test.Server.NextOutcomeAsync()).Confirm();

        Assert.Equal(OutcomeResultEnum.Confirmed, await TestTimes.EndOf(result));
    }

    // "Bare" means without an Outpost-Signature that verifies, whatever the body (§9).
    [Theory]
    [InlineData("refusal-result-too-large")]
    [InlineData("bare")]
    [InlineData("html-page")]
    [InlineData("unsigned-refusal")]
    [InlineData("refusal-signed-by-another-key")]
    public async Task A_verified_result_too_large_and_a_413_without_a_signature_that_verifies_are_TooLarge(string answer)
    {
        using TestConnection test = new();

        Task<OutcomeResultEnum> result = test.SendOutcomeAsync(Guid.NewGuid(), TestOutcomes.Succeeded());
        Give(test, await test.Server.NextOutcomeAsync(), answer);

        Assert.Equal(OutcomeResultEnum.TooLarge, await TestTimes.EndOf(result));
    }

    // A 413 whose signature verifies is no bare 413, and with another code it is no refusal of the table either.
    [Theory]
    [InlineData("confirms-another-command")]
    [InlineData("unsigned-200")]
    [InlineData("content-type-with-charset")]
    [InlineData("refusal-form")]
    [InlineData("refusal-signature")]
    [InlineData("signed-413-with-another-code")]
    [InlineData("protocol-version")]
    [InlineData("bare-403")]
    [InlineData("bare-503")]
    [InlineData("redirect")]
    [InlineData("connection-error")]
    [InlineData("tunnel-refused")]
    public async Task Every_other_answer_is_Lost(string answer)
    {
        using TestConnection test = new();

        Task<OutcomeResultEnum> result = test.SendOutcomeAsync(Guid.NewGuid(), TestOutcomes.Succeeded());
        Give(test, await test.Server.NextOutcomeAsync(), answer);

        Assert.Equal(OutcomeResultEnum.Lost, await TestTimes.EndOf(result));
    }

    // One case for each row of §9's order: no status Unreachable, a proxy's status Answer; a verified 200 Confirmed with
    // its time; a 200 whose signature does not verify Signature, any other unverified 200 Answer; a verified refusal
    // Refusal with its time and code; the unsigned protocol-version body ProtocolVersion; a 401, 403 or 413 without a
    // signature that verifies Bare; anything else Answer. The result is the one of the tests above.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("connection-error", "Lost", false, null, "Unreachable", null)]
    [InlineData("tunnel-refused", "Lost", false, 407, "Answer", null)]
    [InlineData("confirmed", "Confirmed", true, 200, null, null)]
    [InlineData("signed-by-another-key", "Lost", false, 200, "Signature", null)]
    [InlineData("unsigned-200", "Lost", false, 200, "Signature", null)]
    [InlineData("confirms-another-command", "Lost", false, 200, "Answer", null)]
    [InlineData("content-type-with-charset", "Lost", false, 200, "Answer", null)]
    [InlineData("refusal-result-too-large", "TooLarge", true, 413, "Refusal", "result-too-large")]
    [InlineData("refusal-form", "Lost", true, 400, "Refusal", "form")]
    [InlineData("refusal-signature", "Lost", true, 401, "Refusal", "signature")]
    [InlineData("protocol-version", "Lost", false, 400, "ProtocolVersion", null)]
    [InlineData("bare-401", "Lost", false, 401, "Bare", null)]
    [InlineData("bare-403", "Lost", false, 403, "Bare", null)]
    [InlineData("bare", "TooLarge", false, 413, "Bare", null)]
    [InlineData("html-page", "TooLarge", false, 413, "Bare", null)]
    [InlineData("refusal-signed-by-another-key", "TooLarge", false, 413, "Bare", null)]
    [InlineData("signed-413-with-another-code", "Lost", false, 413, "Answer", null)]
    [InlineData("bare-503", "Lost", false, 503, "Answer", null)]
    [InlineData("redirect", "Lost", false, 302, "Answer", null)]
    public async Task Each_answer_to_an_outcome_is_judged_once_with_its_time_its_status_its_cause_and_its_code(
        string answer,
        string result,
        bool timed,
        int? status,
        string? cause,
        string? code)
    {
        using TestConnection test = new();

        Task<OutcomeAnswer> judged = test.AnswerToOutcomeAsync(Guid.NewGuid(), TestOutcomes.Succeeded());
        Give(test, await test.Server.NextOutcomeAsync(), answer);

        OutcomeAnswer expected = new(
            Enum.Parse<OutcomeResultEnum>(result),
            timed ? TestTimes.Start : null,
            status,
            cause is null ? null : Enum.Parse<ServerAnswerCauseEnum>(cause),
            code);
        Assert.Equal(expected, await TestTimes.EndOf(judged));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task An_answer_that_does_not_begin_within_the_request_timeout_is_Unreachable_without_a_status()
    {
        using TestConnection test = new();

        Task<OutcomeAnswer> judged = test.AnswerToOutcomeAsync(Guid.NewGuid(), TestOutcomes.Succeeded());
        await test.Server.NextOutcomeAsync();
        await test.PassExactlyAsync(RequestTimeout);

        Assert.Equal(
            new OutcomeAnswer(OutcomeResultEnum.Lost, null, null, ServerAnswerCauseEnum.Unreachable, null),
            await TestTimes.EndOf(judged));
    }

    // The answer begins after half of requestTimeout and its body stops: the one wait of requestTimeout covers both.
    [Fact]
    public async Task The_wait_for_an_outcome_is_the_request_timeout_over_the_answer_and_its_body()
    {
        using TestConnection test = new();
        DateTimeOffset start = test.Clock.GetUtcNow();
        Guid command = Guid.NewGuid();

        Task<OutcomeResultEnum> result = test.SendOutcomeAsync(command, TestOutcomes.Succeeded());
        ServerExchange exchange = await test.Server.NextOutcomeAsync();
        await test.WaitForTimerAsync(start + RequestTimeout);
        test.Clock.Advance(RequestTimeout / 2);

        byte[] body = OutpostCodec.Write(new OutpostOutcomeAnswer(test.Clock.GetUtcNow(), command));
        StalledStream stalled = exchange.AnswerStalled(body);
        await stalled.Stalled.WaitAsync(TestTimes.Deadline, TestContext.Current.CancellationToken);
        await test.PassExactlyAsync(RequestTimeout / 2);

        Assert.Equal(OutcomeResultEnum.Lost, await TestTimes.EndOf(result));
    }

    // §10: each limit is accepted at it and refused one byte beyond it. White space after the answer keeps §2.
    [Fact]
    public async Task An_answer_of_64_KiB_is_read()
    {
        using TestConnection test = new();
        Guid command = Guid.NewGuid();
        byte[] written = OutpostCodec.Write(new OutpostOutcomeAnswer(test.Clock.GetUtcNow(), command));
        byte[] body = new byte[OutpostProtocol.AnswerLimit];
        written.CopyTo(body, 0);
        body.AsSpan(written.Length).Fill((byte)' ');

        Task<OutcomeResultEnum> result = test.SendOutcomeAsync(command, TestOutcomes.Succeeded());
        (await test.Server.NextOutcomeAsync()).AnswerSigned(200, body);

        Assert.Equal(OutcomeResultEnum.Confirmed, await TestTimes.EndOf(result));
    }

    [Fact]
    public async Task An_answer_declared_beyond_64_KiB_is_lost_unread()
    {
        using TestConnection test = new();
        GeneratedStream body = new(OutpostProtocol.AnswerLimit + 1L);

        Task<OutcomeResultEnum> result = test.SendOutcomeAsync(Guid.NewGuid(), TestOutcomes.Succeeded());
        ServerExchange exchange = await test.Server.NextOutcomeAsync();
        exchange.AnswerStreamed(body, declared: true);

        Assert.Equal(OutcomeResultEnum.Lost, await TestTimes.EndOf(result));
        Assert.Equal(0, body.BytesRead);
    }

    [Fact]
    public async Task An_answer_beyond_64_KiB_that_declares_no_length_is_lost()
    {
        using TestConnection test = new();
        GeneratedStream body = new(OutpostProtocol.AnswerLimit + 1L);

        Task<OutcomeResultEnum> result = test.SendOutcomeAsync(Guid.NewGuid(), TestOutcomes.Succeeded());
        ServerExchange exchange = await test.Server.NextOutcomeAsync();
        exchange.AnswerStreamed(body, declared: false);

        Assert.Equal(OutcomeResultEnum.Lost, await TestTimes.EndOf(result));
    }

    private static void Give(TestConnection test, ServerExchange exchange, string answer)
    {
        Guid command = exchange.ReadOutcome().Command;
        byte[] confirmed = OutpostCodec.Write(new OutpostOutcomeAnswer(test.Clock.GetUtcNow(), command));
        byte[] tooLarge = OutpostCodec.Write(new OutpostRefusal(test.Clock.GetUtcNow(), OutpostProtocolCodes.ResultTooLarge));

        switch (answer)
        {
            case "refusal-result-too-large":
                exchange.Refuse(413, OutpostProtocolCodes.ResultTooLarge);
                break;
            case "bare":
                exchange.AnswerBare(413);
                break;
            case "html-page":
                exchange.AnswerHtml413();
                break;
            case "unsigned-refusal":
                exchange.Answer(ServerExchange.JsonAnswer(413, tooLarge));
                break;
            case "refusal-signed-by-another-key":
                using (ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    exchange.AnswerSignedBy(other, 413, tooLarge);
                }

                break;
            case "confirms-another-command":
                exchange.AnswerSigned(200, OutpostCodec.Write(new OutpostOutcomeAnswer(test.Clock.GetUtcNow(), Guid.NewGuid())));
                break;
            case "unsigned-200":
                exchange.Answer(ServerExchange.JsonAnswer(200, confirmed));
                break;
            case "content-type-with-charset":
                exchange.AnswerSigned(200, confirmed, "application/json; charset=utf-8");
                break;
            case "refusal-form":
                exchange.Refuse(400, OutpostProtocolCodes.Form);
                break;
            case "refusal-signature":
                exchange.Refuse(401, OutpostProtocolCodes.Signature);
                break;
            case "signed-413-with-another-code":
                exchange.Refuse(413, OutpostProtocolCodes.Form);
                break;
            case "protocol-version":
                exchange.AnswerUnsupportedVersion();
                break;
            case "confirmed":
                exchange.Confirm();
                break;
            case "signed-by-another-key":
                using (ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    exchange.AnswerSignedBy(other, 200, confirmed);
                }

                break;
            case "bare-401":
                exchange.AnswerBare(401);
                break;
            case "bare-403":
                exchange.AnswerBare(403);
                break;
            case "bare-503":
                exchange.AnswerBare(503);
                break;
            case "redirect":
                exchange.AnswerRedirect();
                break;
            case "connection-error":
                exchange.Fail(new HttpRequestException("The connection was reset."));
                break;
            case "tunnel-refused":
                exchange.FailTunnelRefused();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(answer), answer, "No such answer.");
        }
    }
}
