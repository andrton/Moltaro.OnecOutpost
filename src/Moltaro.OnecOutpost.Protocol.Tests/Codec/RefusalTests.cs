using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The signed refusals of §9 of docs/protocol.md: `form` under 400, `signature` under 401 and `result-too-large` under
// 413. A verified refusal with any other code, or with one of these under another status, is refused with `form`.
public sealed class RefusalTests : IDisposable
{
    private static readonly byte[] Request = BodySample.OutcomeRequest.ToBytes();

    private readonly ECDsa _serverKey = Sample.NewKey();

    public void Dispose() => _serverKey.Dispose();

    [Theory]
    [InlineData(400, "form")]
    [InlineData(401, "signature")]
    [InlineData(413, "result-too-large")]
    public void A_refusal_with_the_code_of_its_status_is_read(int status, string code)
    {
        OutpostRefusal refusal = Read(status, BodySample.Refusal.With("code", MessageText.Quote(code)).ToBytes());

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 15, 0, 123, TimeSpan.Zero), refusal.Time);
        Assert.Equal(code, refusal.Code);
    }

    [Theory]
    [InlineData(400, "form")]
    [InlineData(401, "signature")]
    [InlineData(413, "result-too-large")]
    public void A_refusal_round_trips(int status, string code)
    {
        OutpostRefusal written = new(new DateTimeOffset(2026, 11, 20, 10, 15, 0, 123, TimeSpan.Zero), code);

        OutpostRefusal read = Read(status, OutpostCodec.Write(written));

        Assert.Equal(written, read);
    }

    [Theory]

    // A code of the table under the status of another.
    [InlineData(400, "signature")]
    [InlineData(400, "result-too-large")]
    [InlineData(401, "form")]
    [InlineData(401, "result-too-large")]
    [InlineData(413, "form")]
    [InlineData(413, "signature")]

    // A status that the table does not hold.
    [InlineData(200, "form")]
    [InlineData(403, "signature")]
    [InlineData(500, "form")]

    // A code that the table does not hold: codes of outcomes, the code of the unsigned 400, and no code.
    [InlineData(400, "protocol-version")]
    [InlineData(400, "expired")]
    [InlineData(400, "malformed")]
    [InlineData(400, "server-key")]
    [InlineData(400, "Form")]
    [InlineData(400, "")]
    public void A_refusal_whose_code_does_not_fit_its_status_is_refused_with_form(int status, string code)
    {
        byte[] body = BodySample.Refusal.With("code", MessageText.Quote(code)).ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(status, body));
    }

    [Fact]
    public void A_refusal_without_a_code_is_refused_with_form()
    {
        byte[] body = BodySample.Refusal.With("code", "null").ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(400, body));
    }

    private OutpostRefusal Read(int status, byte[] body)
    {
        byte[] signature = OutpostSignatures.SignAnswer(
            _serverKey,
            OutpostRequestKindEnum.Outcome,
            Sample.Agent,
            status,
            Request,
            body);

        return OutpostCodec.ReadRefusal(
            _serverKey,
            OutpostRequestKindEnum.Outcome,
            Sample.Agent,
            status,
            Request,
            body,
            signature);
    }
}
