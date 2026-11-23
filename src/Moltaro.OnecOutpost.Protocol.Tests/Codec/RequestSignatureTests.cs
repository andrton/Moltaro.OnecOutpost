using System.Security.Cryptography;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// Every request of an agent is signed over its kind, the identity of its agent and its exact bytes (§3 of
// docs/protocol.md), and the readers of `poll` and of `outcome` verify before they parse. So a body without a valid
// signature is refused with `signature` whatever it holds. The reader of `pair` parses first and has its own tests.
public sealed class RequestSignatureTests : IDisposable
{
    private readonly ECDsa _agentKey = Sample.NewKey();

    private readonly ECDsa _otherKey = Sample.NewKey();

    public void Dispose()
    {
        _agentKey.Dispose();
        _otherKey.Dispose();
    }

    [Theory]
    [InlineData("Poll")]
    [InlineData("Outcome")]
    public void A_request_signed_with_the_key_of_its_agent_is_read(string kind)
    {
        byte[] body = Body(kind);

        Read(kind, Sample.Agent, body, Sign(_agentKey, kind, Sample.Agent, body));
    }

    [Theory]
    [InlineData("Poll")]
    [InlineData("Outcome")]
    public void A_request_whose_bytes_changed_in_any_place_is_refused_with_signature(string kind)
    {
        byte[] body = Body(kind);
        byte[] signature = Sign(_agentKey, kind, Sample.Agent, body);

        for (int place = 0; place < body.Length; place++)
        {
            byte[] changed = [.. body];
            changed[place] ^= 1;

            ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(kind, Sample.Agent, changed, signature));
        }
    }

    [Theory]
    [InlineData("Poll")]
    [InlineData("Outcome")]
    public void A_request_signed_with_another_key_is_refused_with_signature(string kind)
    {
        byte[] body = Body(kind);
        byte[] signature = Sign(_otherKey, kind, Sample.Agent, body);

        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(kind, Sample.Agent, body, signature));
    }

    // Had anything parsed these bytes, the refusal would be `form`.
    [Theory]
    [InlineData("Poll")]
    [InlineData("Outcome")]
    public void A_body_that_does_not_parse_and_carries_no_valid_signature_is_refused_with_signature_never_with_form(
        string kind)
    {
        byte[] body = MessageText.Utf8("not a request");

        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(kind, Sample.Agent, body, new byte[64]));
        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(kind, Sample.Agent, body, []));
        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => Read(kind, Sample.Agent, body, Sign(_otherKey, kind, Sample.Agent, body)));
    }

    [Theory]
    [InlineData("Poll", "Outcome")]
    [InlineData("Poll", "Pair")]
    [InlineData("Outcome", "Poll")]
    [InlineData("Outcome", "Pair")]
    public void A_body_sent_as_another_kind_of_request_is_refused_with_signature(string kind, string signedAs)
    {
        byte[] body = Body(kind);
        byte[] signature = Sign(_agentKey, signedAs, Sample.Agent, body);

        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(kind, Sample.Agent, body, signature));
    }

    [Theory]
    [InlineData("Poll")]
    [InlineData("Outcome")]
    public void The_same_bytes_under_the_identity_of_another_agent_are_refused_with_signature(string kind)
    {
        byte[] body = Body(kind);
        byte[] signature = Sign(_agentKey, kind, Sample.Agent, body);

        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(kind, Sample.OtherAgent, body, signature));
    }

    [Theory]
    [InlineData("Poll")]
    [InlineData("Outcome")]
    public void A_verified_body_that_does_not_parse_is_refused_with_form(string kind)
    {
        byte[] body = MessageText.Utf8("not a request");
        byte[] signature = Sign(_agentKey, kind, Sample.Agent, body);

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(kind, Sample.Agent, body, signature));
    }

    private static byte[] Body(string kind)
    {
        return kind == "Poll" ? BodySample.Poll.ToBytes() : BodySample.OutcomeRequest.ToBytes();
    }

    private static byte[] Sign(ECDsa key, string kind, Guid agent, byte[] body)
    {
        return OutpostSignatures.SignRequest(key, Enum.Parse<OutpostRequestKindEnum>(kind), agent, body);
    }

    private void Read(string kind, Guid agent, byte[] body, byte[] signature)
    {
        if (kind == "Poll")
        {
            OutpostCodec.ReadPollRequest(_agentKey, agent, body, signature);
        }
        else
        {
            OutpostCodec.ReadOutcomeRequest(_agentKey, agent, body, signature, out JsonDocument document);
            document.Dispose();
        }
    }
}
