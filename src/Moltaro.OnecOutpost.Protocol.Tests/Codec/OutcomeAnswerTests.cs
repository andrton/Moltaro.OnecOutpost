using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The body of the answer to an `outcome` (§6.3 of docs/protocol.md): `confirmed` is the request's `command`, and an
// answer with any other is lost.
public sealed class OutcomeAnswerTests : IDisposable
{
    private static readonly byte[] Request = BodySample.OutcomeRequest.ToBytes();

    private readonly ECDsa _serverKey = Sample.NewKey();

    public void Dispose() => _serverKey.Dispose();

    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_answer_that_confirms_the_command_of_the_request_is_read()
    {
        OutpostOutcomeAnswer answer = Read(BodySample.OutcomeAnswer.ToBytes(), Sample.Command);

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 15, 2, 789, TimeSpan.Zero), answer.Time);
        Assert.Equal(Sample.Command, answer.Confirmed);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void An_answer_round_trips()
    {
        OutpostOutcomeAnswer written = new(new DateTimeOffset(2026, 11, 20, 10, 15, 2, 789, TimeSpan.Zero), Sample.Command);

        OutpostOutcomeAnswer read = Read(OutpostCodec.Write(written), Sample.Command);

        Assert.Equal(written, read);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_verified_answer_that_confirms_another_command_is_refused_with_form()
    {
        byte[] body = BodySample.OutcomeAnswer.ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(body, Sample.OtherCommand));
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("\"9C1E2F3A-4B5C-4D6E-8F70-A1B2C3D4E5F6\"")]
    [InlineData("\"{9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6}\"")]
    [InlineData("\"9c1e2f3a4b5c4d6e8f70a1b2c3d4e5f6\"")]
    [InlineData("null")]
    public void An_identity_in_any_other_form_is_refused_with_form(string confirmed)
    {
        byte[] body = BodySample.OutcomeAnswer.With("confirmed", confirmed).ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(body, Sample.Command));
    }

    private OutpostOutcomeAnswer Read(byte[] body, Guid command)
    {
        byte[] signature = OutpostSignatures.SignAnswer(
            _serverKey,
            OutpostRequestKindEnum.Outcome,
            Sample.Agent,
            200,
            Request,
            body);

        return OutpostCodec.ReadOutcomeAnswer(_serverKey, Sample.Agent, command, Request, body, signature);
    }
}
