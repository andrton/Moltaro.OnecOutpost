using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Tests.FakeHttp;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using static Moltaro.OnecOutpost.Agent.Tests.Transport.TestConnection;

namespace Moltaro.OnecOutpost.Agent.Tests.Transport;

// What the agent makes of an answer to a poll (§6.2, §9 and §10 of docs/protocol.md, the S6 brief, "AgentConnection"):
// an answer verified by §9 and ReadPollAnswer, with the sum of the poll's free, is the answer; any other is lost, with
// the reason that connection lost records: the code of a verified refusal, protocol-version for the unsigned body of
// §9, status- and the status of any other answer whose status came, a status that a proxy gave a CONNECT included, or
// unreachable when none came. The agent waits for the answer to begin for the poll timeout of its poll plus the lease
// grace, then reads its body within requestTimeout and 64 MiB, and a body beyond that is lost unread.
public sealed class PollAnswerTests
{
    [Fact]
    public async Task A_verified_200_is_the_answer()
    {
        using TestConnection test = new();
        OutpostSignedCommand command = test.Server.Sign(TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, TestTimes.Start));
        OutpostPollAnswer sent = test.Server.Ordinary(command) with
        {
            PollTimeout = TimeSpan.FromSeconds(40),
            LeaseGrace = TimeSpan.FromSeconds(15),
            Held = ["accounting"],
            Claimed = ["warehouse"],
        };

        Task<PollResult> result = test.PollAsync(test.Poll());
        (await test.Server.NextPollAsync()).AnswerPoll(sent);
        PollResult answer = await EndOf(result);

        Assert.Null(answer.Reason);
        OutpostPollAnswer read = Assert.IsType<OutpostPollAnswer>(answer.Answer);
        Assert.Equal(sent.Time, read.Time);
        Assert.Null(read.Newest);
        Assert.Equal(sent.PollTimeout, read.PollTimeout);
        Assert.Equal(sent.LeaseGrace, read.LeaseGrace);
        Assert.Equal<string>(["accounting"], read.Held);
        Assert.Equal<string>(["warehouse"], read.Claimed);
        OutpostSignedCommand handed = Assert.Single(read.Commands);
        Assert.Equal(command.Bytes, handed.Bytes);
        Assert.Equal(command.Signature, handed.Signature);
    }

    // §9: only an answer that the table names is acted on; every other answer to a poll is lost. An answer is verified
    // when its Content-Type is exactly application/json, its body is within the read limit, its Outpost-Signature
    // verifies for this poll and its status, and its body keeps §2 and §6.2.
    [Theory]
    [InlineData("connection-error", "unreachable")]
    [InlineData("tunnel-refused", "status-407")]
    [InlineData("redirect", "status-302")]
    [InlineData("unsigned-200", "status-200")]
    [InlineData("content-type-with-charset", "status-200")]
    [InlineData("signed-by-another-key", "status-200")]
    [InlineData("signed-for-another-status", "status-200")]
    [InlineData("breaks-its-message", "status-200")]
    [InlineData("more-commands-than-free", "status-200")]
    [InlineData("refusal-form", "form")]
    [InlineData("refusal-signature", "signature")]
    [InlineData("refusal-result-too-large", "result-too-large")]
    [InlineData("refusal-under-another-status", "status-400")]
    [InlineData("protocol-version", "protocol-version")]
    [InlineData("bare-401", "status-401")]
    [InlineData("bare-403", "status-403")]
    [InlineData("bare-413", "status-413")]
    [InlineData("bare-503", "status-503")]
    public async Task Every_other_answer_is_lost_with_the_reason_of_section_9(string answer, string reason)
    {
        using TestConnection test = new();

        Task<PollResult> result = test.PollAsync(test.Poll());
        Give(test, await test.Server.NextPollAsync(), answer);
        PollResult lost = await EndOf(result);

        Assert.Null(lost.Answer);
        Assert.Equal(reason, lost.Reason);
    }

    [Fact]
    public async Task An_answer_that_does_not_begin_within_the_poll_timeout_plus_the_lease_grace_is_lost_as_unreachable()
    {
        using TestConnection test = new();

        Task<PollResult> result = test.PollAsync(test.Poll());
        await test.Server.NextPollAsync();
        await test.PassExactlyAsync(FakeServer.PollTimeout + FakeServer.LeaseGrace);
        PollResult lost = await EndOf(result);

        Assert.Null(lost.Answer);
        Assert.Equal("unreachable", lost.Reason);
    }

    // The answer begins after 20 of its 35 seconds; its body then has requestTimeout, 10 seconds, from then.
    [Fact]
    public async Task The_body_of_an_answer_that_began_is_read_within_the_request_timeout_or_lost_with_its_status()
    {
        using TestConnection test = new();
        DateTimeOffset start = test.Clock.GetUtcNow();

        Task<PollResult> result = test.PollAsync(test.Poll());
        ServerExchange exchange = await test.Server.NextPollAsync();
        await test.WaitForTimerAsync(start + FakeServer.PollTimeout + FakeServer.LeaseGrace);
        test.Clock.Advance(TimeSpan.FromSeconds(20));

        byte[] body = OutpostCodec.Write(test.Server.Ordinary());
        exchange.AnswerStalled(body);
        await test.PassExactlyAsync(RequestTimeout);
        PollResult lost = await EndOf(result);

        Assert.Null(lost.Answer);
        Assert.Equal("status-200", lost.Reason);
    }

    // §10: each limit is accepted at it and refused one byte beyond it. White space after the answer keeps §2.
    [Fact]
    public async Task A_poll_answer_of_64_MiB_is_read()
    {
        using TestConnection test = new();
        byte[] written = OutpostCodec.Write(test.Server.Ordinary());
        byte[] body = new byte[OutpostProtocol.PollAnswerLimit];
        written.CopyTo(body, 0);
        body.AsSpan(written.Length).Fill((byte)' ');

        Task<PollResult> result = test.PollAsync(test.Poll());
        (await test.Server.NextPollAsync()).AnswerSigned(200, body);
        PollResult answer = await EndOf(result);

        Assert.NotNull(answer.Answer);
    }

    [Fact]
    public async Task A_poll_answer_declared_beyond_64_MiB_is_lost_unread()
    {
        using TestConnection test = new();
        GeneratedStream body = new(OutpostProtocol.PollAnswerLimit + 1L);

        Task<PollResult> result = test.PollAsync(test.Poll());
        ServerExchange exchange = await test.Server.NextPollAsync();
        exchange.AnswerStreamed(body, declared: true);
        PollResult lost = await EndOf(result);

        Assert.Null(lost.Answer);
        Assert.Equal("status-200", lost.Reason);
        Assert.Equal(0, body.BytesRead);
    }

    [Fact]
    public async Task A_poll_answer_beyond_64_MiB_that_declares_no_length_is_lost()
    {
        using TestConnection test = new();
        GeneratedStream body = new(OutpostProtocol.PollAnswerLimit + 1L);

        Task<PollResult> result = test.PollAsync(test.Poll());
        ServerExchange exchange = await test.Server.NextPollAsync();
        exchange.AnswerStreamed(body, declared: false);
        PollResult lost = await EndOf(result);

        Assert.Null(lost.Answer);
        Assert.Equal("status-200", lost.Reason);
    }

    private static void Give(TestConnection test, ServerExchange exchange, string answer)
    {
        byte[] ordinary = OutpostCodec.Write(test.Server.Ordinary());

        switch (answer)
        {
            case "connection-error":
                exchange.Fail(new HttpRequestException("The connection was reset."));
                break;
            case "tunnel-refused":
                exchange.FailTunnelRefused();
                break;
            case "redirect":
                exchange.AnswerRedirect();
                break;
            case "unsigned-200":
                exchange.Answer(ServerExchange.JsonAnswer(200, ordinary));
                break;
            case "content-type-with-charset":
                exchange.AnswerSigned(200, ordinary, "application/json; charset=utf-8");
                break;
            case "signed-by-another-key":
                using (ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    exchange.AnswerSignedBy(other, 200, ordinary);
                }

                break;
            case "signed-for-another-status":
                exchange.Answer(ServerExchange.Signed(ServerExchange.JsonAnswer(200, ordinary), exchange.SignatureOf(201, ordinary)));
                break;
            case "breaks-its-message":
                exchange.AnswerSigned(200, "{\"time\":\"2026-11-22T10:15:00.123Z\"}"u8.ToArray());
                break;
            case "more-commands-than-free":
                OutpostSignedCommand[] commands =
                [
                    .. Enumerable.Range(0, 4).Select(_ => test.Server.Sign(TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, TestTimes.Start))),
                ];
                exchange.AnswerPoll(test.Server.Ordinary(commands));
                break;
            case "refusal-form":
                exchange.Refuse(400, OutpostProtocolCodes.Form);
                break;
            case "refusal-signature":
                exchange.Refuse(401, OutpostProtocolCodes.Signature);
                break;
            case "refusal-result-too-large":
                exchange.Refuse(413, OutpostProtocolCodes.ResultTooLarge);
                break;
            case "refusal-under-another-status":
                exchange.Refuse(400, OutpostProtocolCodes.Signature);
                break;
            case "protocol-version":
                exchange.AnswerUnsupportedVersion();
                break;
            case "bare-401":
                exchange.AnswerBare(401);
                break;
            case "bare-403":
                exchange.AnswerBare(403);
                break;
            case "bare-413":
                exchange.AnswerBare(413);
                break;
            case "bare-503":
                exchange.AnswerBare(503);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(answer), answer, "No such answer.");
        }
    }
}
