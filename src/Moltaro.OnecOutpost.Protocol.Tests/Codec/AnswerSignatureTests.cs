using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// Every answer that an agent acts on is signed by the server's key over its kind, the identity of the agent, its
// status, the hash of the body of the one request it answers and its own exact bytes (§3 of docs/protocol.md). The
// readers of the answer to a poll, of the answer to an outcome and of a signed refusal verify before they parse, so
// an answer that is not the server's, or not for this request, is refused with `signature`. The reader of the answer
// to `pair` parses first and has its own tests.
//
// The three answers of these tests are a `200` to a poll, a `200` to an outcome and a refusal `400` of a poll.
public sealed class AnswerSignatureTests : IDisposable
{
    private readonly ECDsa _serverKey = Sample.NewKey();

    private readonly ECDsa _otherKey = Sample.NewKey();

    public void Dispose()
    {
        _serverKey.Dispose();
        _otherKey.Dispose();
    }

    [Theory]
    [InlineData("poll")]
    [InlineData("outcome")]
    [InlineData("refusal")]
    public void An_answer_signed_by_the_server_for_this_request_is_read(string answer)
    {
        byte[] body = Body(answer);

        Read(answer, Sample.Agent, Request(answer), body, Sign(_serverKey, answer, body));
    }

    [Theory]
    [InlineData("poll")]
    [InlineData("outcome")]
    [InlineData("refusal")]
    public void An_answer_whose_bytes_changed_in_any_place_is_refused_with_signature(string answer)
    {
        byte[] body = Body(answer);
        byte[] signature = Sign(_serverKey, answer, body);

        for (int place = 0; place < body.Length; place++)
        {
            byte[] changed = [.. body];
            changed[place] ^= 1;

            ProtocolAssert.Refused(
                OutpostProtocolCodes.Signature,
                () => Read(answer, Sample.Agent, Request(answer), changed, signature));
        }
    }

    [Theory]
    [InlineData("poll")]
    [InlineData("outcome")]
    [InlineData("refusal")]
    public void An_answer_signed_with_another_key_is_refused_with_signature(string answer)
    {
        byte[] body = Body(answer);
        byte[] signature = Sign(_otherKey, answer, body);

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => Read(answer, Sample.Agent, Request(answer), body, signature));
    }

    [Theory]
    [InlineData("poll")]
    [InlineData("outcome")]
    [InlineData("refusal")]
    public void An_answer_bound_to_another_request_is_refused_with_signature(string answer)
    {
        byte[] body = Body(answer);
        byte[] signature = Sign(_serverKey, answer, body);

        // The same request with one more space after it: another body, so another hash.
        byte[] otherRequest = [.. Request(answer), (byte)' '];

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => Read(answer, Sample.Agent, otherRequest, body, signature));
    }

    [Theory]
    [InlineData("poll")]
    [InlineData("outcome")]
    [InlineData("refusal")]
    public void An_answer_bound_to_another_agent_is_refused_with_signature(string answer)
    {
        byte[] body = Body(answer);
        byte[] signature = Sign(_serverKey, answer, body);

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => Read(answer, Sample.OtherAgent, Request(answer), body, signature));
    }

    [Theory]

    // An answer `200` signed under the status of a refusal, and a refusal signed under another status than it came
    // with.
    [InlineData("poll", 400)]
    [InlineData("outcome", 413)]
    [InlineData("refusal", 200)]
    [InlineData("refusal", 401)]
    public void An_answer_bound_to_another_status_is_refused_with_signature(string answer, int signedStatus)
    {
        byte[] body = Body(answer);
        byte[] signature = OutpostSignatures.SignAnswer(
            _serverKey,
            Kind(answer),
            Sample.Agent,
            signedStatus,
            Request(answer),
            body);

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => Read(answer, Sample.Agent, Request(answer), body, signature));
    }

    [Theory]
    [InlineData("poll", "Outcome")]
    [InlineData("poll", "Pair")]
    [InlineData("outcome", "Poll")]
    [InlineData("refusal", "Outcome")]
    public void An_answer_bound_to_another_kind_of_request_is_refused_with_signature(string answer, string signedKind)
    {
        byte[] body = Body(answer);
        byte[] signature = OutpostSignatures.SignAnswer(
            _serverKey,
            Enum.Parse<OutpostRequestKindEnum>(signedKind),
            Sample.Agent,
            Status(answer),
            Request(answer),
            body);

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => Read(answer, Sample.Agent, Request(answer), body, signature));
    }

    // Had anything parsed these bytes, the refusal would be `form`.
    [Theory]
    [InlineData("poll")]
    [InlineData("outcome")]
    [InlineData("refusal")]
    public void A_body_that_does_not_parse_and_carries_no_valid_signature_is_refused_with_signature_never_with_form(
        string answer)
    {
        byte[] body = MessageText.Utf8("<html>413 Request Entity Too Large</html>");

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => Read(answer, Sample.Agent, Request(answer), body, new byte[64]));
        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => Read(answer, Sample.Agent, Request(answer), body, []));
    }

    [Theory]
    [InlineData("poll")]
    [InlineData("outcome")]
    [InlineData("refusal")]
    public void A_verified_body_that_does_not_parse_is_refused_with_form(string answer)
    {
        byte[] body = MessageText.Utf8("not an answer");
        byte[] signature = Sign(_serverKey, answer, body);

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Form,
            () => Read(answer, Sample.Agent, Request(answer), body, signature));
    }

    private static byte[] Body(string answer)
    {
        return answer switch
        {
            "poll" => BodySample.PollAnswer.ToBytes(),
            "outcome" => BodySample.OutcomeAnswer.ToBytes(),
            _ => BodySample.Refusal.ToBytes(),
        };
    }

    private static byte[] Request(string answer)
    {
        return answer == "outcome" ? BodySample.OutcomeRequest.ToBytes() : BodySample.Poll.ToBytes();
    }

    private static OutpostRequestKindEnum Kind(string answer)
    {
        return answer == "outcome" ? OutpostRequestKindEnum.Outcome : OutpostRequestKindEnum.Poll;
    }

    private static int Status(string answer)
    {
        return answer == "refusal" ? 400 : 200;
    }

    private static byte[] Sign(ECDsa key, string answer, byte[] body)
    {
        return OutpostSignatures.SignAnswer(key, Kind(answer), Sample.Agent, Status(answer), Request(answer), body);
    }

    private void Read(string answer, Guid agent, byte[] request, byte[] body, byte[] signature)
    {
        switch (answer)
        {
            case "poll":
                OutpostCodec.ReadPollAnswer(_serverKey, agent, 2, request, body, signature);
                break;

            case "outcome":
                OutpostCodec.ReadOutcomeAnswer(_serverKey, agent, Sample.Command, request, body, signature);
                break;

            default:
                OutpostCodec.ReadRefusal(_serverKey, OutpostRequestKindEnum.Poll, agent, 400, request, body, signature);
                break;
        }
    }
}
