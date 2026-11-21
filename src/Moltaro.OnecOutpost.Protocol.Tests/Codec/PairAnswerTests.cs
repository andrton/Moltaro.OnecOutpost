using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The body of the answer to `pair` (§6.1 of docs/protocol.md). Its reader is the one reader of the agent that parses
// before it verifies, because the server's key is in the answer (§3). In this order: no server key can be read, `form`;
// the key does not match the fingerprint of the token, `server-key`; the signature does not verify with that key,
// `signature`; the answer breaks §2 or §6.1, or its `agent` or `key` is not what the agent sent, `form`.
public sealed class PairAnswerTests : IDisposable
{
    // The body of the `pair` request that the answer answers; the reader only hashes it.
    private static readonly byte[] Request = MessageText.Utf8("""{"agentVersion":"0.1.0","key":"MFkw","proof":"u3Fq"}""");

    private readonly ECDsa _serverKey = Sample.NewKey();

    private readonly ECDsa _otherKey = Sample.NewKey();

    private readonly byte[] _sentKey;

    private readonly string _fingerprint;

    public PairAnswerTests()
    {
        using ECDsa agentKey = Sample.NewKey();

        _sentKey = agentKey.ExportSubjectPublicKeyInfo();
        _fingerprint = Fingerprint(_serverKey);
    }

    // A member with a range, a value of it, and whether the value is within the range. Every range includes its ends.
    public static TheoryData<string, string, bool> Ranges => new()
    {
        { "pollTimeoutMs", Milliseconds(OutpostProtocol.MinPollTimeout, -1), false },
        { "pollTimeoutMs", Milliseconds(OutpostProtocol.MinPollTimeout), true },
        { "pollTimeoutMs", Milliseconds(OutpostProtocol.MaxPollTimeout), true },
        { "pollTimeoutMs", Milliseconds(OutpostProtocol.MaxPollTimeout, 1), false },

        { "leaseGraceMs", Milliseconds(OutpostProtocol.MinLeaseGrace, -1), false },
        { "leaseGraceMs", Milliseconds(OutpostProtocol.MinLeaseGrace), true },
        { "leaseGraceMs", Milliseconds(OutpostProtocol.MaxLeaseGrace), true },
        { "leaseGraceMs", Milliseconds(OutpostProtocol.MaxLeaseGrace, 1), false },
    };

    public void Dispose()
    {
        _serverKey.Dispose();
        _otherKey.Dispose();
    }

    [Fact]
    public void The_answer_of_the_server_that_the_token_names_is_read_into_its_members()
    {
        byte[] body = Body().ToBytes();

        OutpostPairAnswer answer = Read(body, Sign(_serverKey, body));

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 15, 0, 123, TimeSpan.Zero), answer.Time);
        Assert.Equal(Sample.Agent, answer.Agent);
        Assert.Equal(_sentKey, answer.Key);
        Assert.Equal(_serverKey.ExportSubjectPublicKeyInfo(), answer.ServerKey);
        Assert.Equal(TimeSpan.FromSeconds(25), answer.PollTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), answer.LeaseGrace);
    }

    [Fact]
    public void An_answer_round_trips()
    {
        OutpostPairAnswer written = new(
            new DateTimeOffset(2026, 11, 20, 10, 15, 0, 123, TimeSpan.Zero),
            Sample.Agent,
            _sentKey,
            _serverKey.ExportSubjectPublicKeyInfo(),
            TimeSpan.FromSeconds(25),
            TimeSpan.FromSeconds(10));

        byte[] body = OutpostCodec.Write(written);
        OutpostPairAnswer read = Read(body, Sign(_serverKey, body));

        Assert.Equal(written.Time, read.Time);
        Assert.Equal(written.Agent, read.Agent);
        Assert.Equal(written.Key, read.Key);
        Assert.Equal(written.ServerKey, read.ServerKey);
        Assert.Equal(written.PollTimeout, read.PollTimeout);
        Assert.Equal(written.LeaseGrace, read.LeaseGrace);
    }

    // Something that opens TLS on the path can answer with a key of its own and sign with it: the signature verifies,
    // and only the fingerprint of the token tells.
    [Fact]
    public void An_answer_whose_server_key_does_not_match_the_fingerprint_of_the_token_is_refused_with_server_key_even_when_it_is_signed_with_that_key()
    {
        byte[] body = Body().With("serverKey", KeyText(_otherKey)).ToBytes();
        byte[] signature = Sign(_otherKey, body);

        ProtocolAssert.Refused(OutpostProtocolCodes.ServerKey, () => Read(body, signature));
    }

    [Fact]
    public void The_fingerprint_is_checked_before_the_signature()
    {
        byte[] body = Body().With("serverKey", KeyText(_otherKey)).ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.ServerKey, () => Read(body, new byte[64]));
    }

    [Fact]
    public void An_answer_with_the_right_server_key_and_a_wrong_signature_is_refused_with_signature()
    {
        byte[] body = Body().ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(body, Sign(_otherKey, body)));
        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(body, new byte[64]));
        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(body, []));
    }

    [Fact]
    public void An_answer_whose_bytes_changed_is_refused_with_signature()
    {
        byte[] signature = Sign(_serverKey, Body().ToBytes());
        byte[] changed = Body().With("leaseGraceMs", "10001").ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(changed, signature));
    }

    [Fact]
    public void An_answer_bound_to_another_request_is_refused_with_signature()
    {
        byte[] body = Body().ToBytes();
        byte[] otherRequest = [.. Request, (byte)' '];
        byte[] signature = OutpostSignatures.SignAnswer(
            _serverKey,
            OutpostRequestKindEnum.Pair,
            Sample.Agent,
            200,
            otherRequest,
            body);

        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(body, signature));
    }

    [Fact]
    public void An_answer_bound_to_another_agent_is_refused_with_signature()
    {
        byte[] body = Body().ToBytes();
        byte[] signature = OutpostSignatures.SignAnswer(
            _serverKey,
            OutpostRequestKindEnum.Pair,
            Sample.OtherAgent,
            200,
            Request,
            body);

        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(body, signature));
    }

    [Theory]
    [InlineData("Pair", 403)]
    [InlineData("Poll", 200)]
    [InlineData("Outcome", 200)]
    public void An_answer_bound_to_another_status_or_kind_of_request_is_refused_with_signature(string kind, int status)
    {
        byte[] body = Body().ToBytes();
        byte[] signature = OutpostSignatures.SignAnswer(
            _serverKey,
            Enum.Parse<OutpostRequestKindEnum>(kind),
            Sample.Agent,
            status,
            Request,
            body);

        ProtocolAssert.Refused(OutpostProtocolCodes.Signature, () => Read(body, signature));
    }

    [Fact]
    public void A_signed_answer_whose_agent_is_not_the_one_that_was_sent_is_refused_with_form()
    {
        byte[] body = Body().With("agent", MessageText.Quote(Sample.OtherAgentText)).ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(body, Sign(_serverKey, body)));
    }

    [Fact]
    public void A_signed_answer_whose_key_is_not_the_one_that_was_sent_is_refused_with_form()
    {
        byte[] body = Body().With("key", KeyText(_otherKey)).ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(body, Sign(_serverKey, body)));
    }

    [Fact]
    public void An_answer_without_a_readable_server_key_is_refused_with_form()
    {
        byte[] key = _serverKey.ExportSubjectPublicKeyInfo();
        string keyText = MessageText.Base64Url(key);

        byte[][] bodies =
        [
            MessageText.Utf8("<html>403 Forbidden</html>"),
            MessageText.Utf8("[]"),
            Body().Without("serverKey").ToBytes(),
            Body().With("serverKey", "null").ToBytes(),
            Body().With("serverKey", "17").ToBytes(),
            Body().With("serverKey", "\"not a key\"").ToBytes(),

            // A key cut short, one with a byte beyond its structure, and one that is not canonical base64url.
            Body().With("serverKey", MessageText.Quote(MessageText.Base64Url(key.AsSpan(0, 90)))).ToBytes(),
            Body().With("serverKey", MessageText.Quote(MessageText.Base64Url([.. key, 0]))).ToBytes(),
            Body().With("serverKey", MessageText.Quote(keyText + "==")).ToBytes(),
        ];

        foreach (byte[] body in bodies)
        {
            ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(body, Sign(_serverKey, body)));
        }
    }

    [Fact]
    public void A_signed_answer_that_has_not_exactly_its_members_is_refused_with_form()
    {
        byte[] withoutTime = Body().Without("time").ToBytes();
        byte[] withAnother = Body().With("name", "\"back office\"").ToBytes();
        byte[] withATimeInAnotherForm = Body().With("time", "\"2026-11-20T10:15:00Z\"").ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(withoutTime, Sign(_serverKey, withoutTime)));
        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(withAnother, Sign(_serverKey, withAnother)));
        ProtocolAssert.Refused(
            OutpostProtocolCodes.Form,
            () => Read(withATimeInAnotherForm, Sign(_serverKey, withATimeInAnotherForm)));
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void A_member_is_accepted_at_the_ends_of_its_range_and_refused_one_step_beyond_them(
        string member,
        string value,
        bool within)
    {
        byte[] body = Body().With(member, value).ToBytes();
        byte[] signature = Sign(_serverKey, body);

        if (within)
        {
            Read(body, signature);
        }
        else
        {
            ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(body, signature));
        }
    }

    private static string Milliseconds(TimeSpan duration, int step = 0)
    {
        return ((long)duration.TotalMilliseconds + step).ToString();
    }

    private static string KeyText(ECDsa key)
    {
        return MessageText.Quote(MessageText.Base64Url(key.ExportSubjectPublicKeyInfo()));
    }

    // The fingerprint of §2, made without the codec.
    private static string Fingerprint(ECDsa key)
    {
        return Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
    }

    private static byte[] Sign(ECDsa key, byte[] body)
    {
        return OutpostSignatures.SignAnswer(key, OutpostRequestKindEnum.Pair, Sample.Agent, 200, Request, body);
    }

    private MessageText Body()
    {
        return new MessageText(
            ("time", "\"2026-11-20T10:15:00.123Z\""),
            ("agent", MessageText.Quote(Sample.AgentText)),
            ("key", MessageText.Quote(MessageText.Base64Url(_sentKey))),
            ("serverKey", KeyText(_serverKey)),
            ("pollTimeoutMs", "25000"),
            ("leaseGraceMs", "10000"));
    }

    private OutpostPairAnswer Read(byte[] body, byte[] signature)
    {
        return OutpostCodec.ReadPairAnswer(_fingerprint, Sample.Agent, _sentKey, Request, body, signature);
    }
}
