using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The body of a `pair` (§6.1 of docs/protocol.md): the version of the agent, its new public key and the pairing proof.
// Its reader is the one reader of the server that parses before it verifies, because the key to verify with is in the
// body (§3): it refuses a body that breaks §2 or §6.1 with `form`, and then a signature that the key of the body does
// not verify with `signature`. The proof needs the secret of the token, and is not the reader's.
public sealed class PairRequestTests : IDisposable
{
    private readonly ECDsa _agentKey = Sample.NewKey();

    private readonly ECDsa _otherKey = Sample.NewKey();

    private readonly byte[] _proof = RandomNumberGenerator.GetBytes(32);

    public void Dispose()
    {
        _agentKey.Dispose();
        _otherKey.Dispose();
    }

    [Fact]
    public void A_request_signed_with_the_key_it_carries_is_read_into_its_members()
    {
        byte[] body = Body().ToBytes();

        OutpostPairRequest request = OutpostCodec.ReadPairRequest(Sample.Agent, body, Sign(_agentKey, body));

        Assert.Equal("0.1.0", request.AgentVersion);
        Assert.Equal(_agentKey.ExportSubjectPublicKeyInfo(), request.Key);
        Assert.Equal(_proof, request.Proof);
    }

    [Fact]
    public void A_request_round_trips()
    {
        OutpostPairRequest written = new("0.1.0-rc.1+build-7", _agentKey.ExportSubjectPublicKeyInfo(), _proof);

        byte[] body = OutpostCodec.Write(written);
        OutpostPairRequest read = OutpostCodec.ReadPairRequest(Sample.Agent, body, Sign(_agentKey, body));

        Assert.Equal(written.AgentVersion, read.AgentVersion);
        Assert.Equal(written.Key, read.Key);
        Assert.Equal(written.Proof, read.Proof);
    }

    [Fact]
    public void A_request_signed_with_another_key_than_the_one_it_carries_is_refused_with_signature()
    {
        byte[] body = Body().ToBytes();
        byte[] signature = Sign(_otherKey, body);

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => OutpostCodec.ReadPairRequest(Sample.Agent, body, signature));
    }

    // The proof was made for one key; a request that carries another key no longer verifies with the signature that
    // the first key made.
    [Fact]
    public void A_request_whose_key_was_swapped_after_it_was_signed_is_refused_with_signature()
    {
        byte[] signature = Sign(_agentKey, Body().ToBytes());
        byte[] swapped = Body()
            .With("key", MessageText.Quote(MessageText.Base64Url(_otherKey.ExportSubjectPublicKeyInfo())))
            .ToBytes();

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => OutpostCodec.ReadPairRequest(Sample.Agent, swapped, signature));
    }

    [Fact]
    public void A_request_whose_bytes_changed_is_refused_with_signature()
    {
        byte[] signature = Sign(_agentKey, Body().ToBytes());
        byte[] changed = Body().With("agentVersion", "\"0.1.1\"").ToBytes();

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => OutpostCodec.ReadPairRequest(Sample.Agent, changed, signature));
    }

    [Theory]
    [InlineData("Poll")]
    [InlineData("Outcome")]
    public void A_body_signed_as_another_kind_of_request_is_refused_with_signature(string signedAs)
    {
        byte[] body = Body().ToBytes();
        byte[] signature = OutpostSignatures.SignRequest(
            _agentKey,
            Enum.Parse<OutpostRequestKindEnum>(signedAs),
            Sample.Agent,
            body);

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => OutpostCodec.ReadPairRequest(Sample.Agent, body, signature));
    }

    [Fact]
    public void The_same_bytes_under_another_identity_are_refused_with_signature()
    {
        byte[] body = Body().ToBytes();
        byte[] signature = Sign(_agentKey, body);

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => OutpostCodec.ReadPairRequest(Sample.OtherAgent, body, signature));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    [InlineData(64)]
    public void A_request_without_a_valid_signature_is_refused_with_signature(int signatureLength)
    {
        byte[] body = Body().ToBytes();

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Signature,
            () => OutpostCodec.ReadPairRequest(Sample.Agent, body, new byte[signatureLength]));
    }

    // The one request that is parsed before it is verified: there is no key to verify these bytes with.
    [Fact]
    public void A_body_that_does_not_parse_is_refused_with_form_whatever_its_signature()
    {
        byte[] body = MessageText.Utf8("not a request");

        ProtocolAssert.Refused(
            OutpostProtocolCodes.Form,
            () => OutpostCodec.ReadPairRequest(Sample.Agent, body, new byte[64]));
    }

    [Fact]
    public void A_key_that_is_not_canonical_base64url_is_refused_with_form()
    {
        // The 91 bytes of a key are 122 characters, the last with four padding bits; here they are not zero.
        string key = MessageText.Base64Url(_agentKey.ExportSubjectPublicKeyInfo());
        string notCanonical = key[..^1] + (char)(key[^1] + 1);

        AssertForm(Body().With("key", MessageText.Quote(notCanonical)));
        AssertForm(Body().With("key", MessageText.Quote(key + "==")));
    }

    [Fact]
    public void A_key_that_is_not_the_91_byte_form_is_refused_with_form()
    {
        using ECDsa otherCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        byte[] key = _agentKey.ExportSubjectPublicKeyInfo();

        AssertForm(Body().With("key", MessageText.Quote(MessageText.Base64Url(otherCurve.ExportSubjectPublicKeyInfo()))));
        AssertForm(Body().With("key", MessageText.Quote(MessageText.Base64Url(key.AsSpan(0, 90)))));
        AssertForm(Body().With("key", MessageText.Quote(MessageText.Base64Url([.. key, 0]))));
        AssertForm(Body().With("key", "null"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void A_proof_that_is_not_32_bytes_is_refused_with_form(int length)
    {
        AssertForm(Body().With("proof", MessageText.Quote(MessageText.Base64Url(new byte[length]))));
    }

    [Fact]
    public void A_proof_that_is_not_canonical_base64url_is_refused_with_form()
    {
        // 32 bytes are 43 characters, the last with two padding bits: A keeps them zero, and B does not.
        AssertForm(Body().With("proof", MessageText.Quote(new string('A', 42) + "B")));
        AssertForm(Body().With("proof", MessageText.Quote(new string('A', 43) + "=")));
    }

    // The rule of agent versions is stated in full on the poll; the reader of `pair` applies it.
    [Theory]
    [InlineData("")]
    [InlineData("0.1 0")]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234")]
    public void An_agent_version_that_breaks_its_rule_is_refused_with_form(string version)
    {
        AssertForm(Body().With("agentVersion", MessageText.Quote(version)));
    }

    [Fact]
    public void A_request_that_has_not_exactly_its_three_members_is_refused_with_form()
    {
        AssertForm(Body().Without("agentVersion"));
        AssertForm(Body().Without("proof"));
        AssertForm(Body().With("agent", MessageText.Quote(Sample.AgentText)));
    }

    private MessageText Body()
    {
        return new MessageText(
            ("agentVersion", "\"0.1.0\""),
            ("key", MessageText.Quote(MessageText.Base64Url(_agentKey.ExportSubjectPublicKeyInfo()))),
            ("proof", MessageText.Quote(MessageText.Base64Url(_proof))));
    }

    private static byte[] Sign(ECDsa key, byte[] body)
    {
        return OutpostSignatures.SignRequest(key, OutpostRequestKindEnum.Pair, Sample.Agent, body);
    }

    // The body is signed with the key of the agent, so that only its form is at fault where it still carries that key.
    private void AssertForm(MessageText text)
    {
        byte[] body = text.ToBytes();
        byte[] signature = Sign(_agentKey, body);

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => OutpostCodec.ReadPairRequest(Sample.Agent, body, signature));
    }
}
