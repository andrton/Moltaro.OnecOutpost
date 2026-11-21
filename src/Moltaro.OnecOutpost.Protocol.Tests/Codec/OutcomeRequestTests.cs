using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The body of an `outcome` (§6.3 of docs/protocol.md): the identity of a command and its outcome object, as the exact
// bytes that the agent forwards. The reader checks the whole body by §2, the outcome object included, and refuses a
// body that breaks §2 with `form` before anything looks at the size of the outcome (step 1 of §6.3). The size and the
// member rules of §8 follow it, and are not this reader's.
public sealed class OutcomeRequestTests : IDisposable
{
    // An outcome object in a form of its own, as the executor may write it: white space, and escapes that the writers
    // of the library do not write.
    private const string OutcomeOfTheExecutor =
        """{ "kind" : "succeeded", "code" : null, "result" : { "name" : "\u00e9\/" }, "errorText" : null, "messages" : [ "one" ], "messagesTrimmed" : false }""";

    private readonly ECDsa _agentKey = Sample.NewKey();

    public static TheoryData<string> OutcomesThatBreakTheFormOfMessages =>
    [
        // A duplicated member, at the top of the object and deep inside its result, the names compared after unescaping.
        """{"kind":"succeeded","kind":"succeeded","code":null,"result":null,"errorText":null,"messages":[],"messagesTrimmed":false}""",
        """{"kind":"succeeded","code":null,"result":{"a":[{"b":1,"b":2}]},"errorText":null,"messages":[],"messagesTrimmed":false}""",
        """{"kind":"succeeded","code":null,"result":{"a":{"b":1,"\u0062":2}},"errorText":null,"messages":[],"messagesTrimmed":false}""",

        // U+0000 and a lone surrogate in a string.
        """{"kind":"succeeded","code":null,"result":"\u0000","errorText":null,"messages":[],"messagesTrimmed":false}""",
        """{"kind":"succeeded","code":null,"result":null,"errorText":null,"messages":["\ud83d"],"messagesTrimmed":false}""",

        // A text that is not JSON.
        """{"kind":"succeeded","code":null,"result":{,"errorText":null,"messages":[],"messagesTrimmed":false}""",

        // No outcome object.
        "null",
    ];

    public void Dispose() => _agentKey.Dispose();

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_request_is_read_into_the_identity_of_its_command_and_the_exact_bytes_of_its_outcome()
    {
        OutpostOutcomeRequest request = Read(BodySample.OutcomeRequest.With("outcome", OutcomeOfTheExecutor).ToBytes());

        Assert.Equal(Sample.Command, request.Command);
        Assert.Equal(MessageText.Utf8(OutcomeOfTheExecutor), request.Outcome.ToArray());
    }

    // The size of an outcome is the length of these bytes, and they may be 64 MiB: the reader does not copy them.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_outcome_is_kept_as_a_slice_of_the_body_without_a_copy()
    {
        byte[] body = BodySample.OutcomeRequest.ToBytes();

        OutpostOutcomeRequest request = Read(body);

        Assert.True(MemoryMarshal.TryGetArray(request.Outcome, out ArraySegment<byte> slice));
        Assert.Same(body, slice.Array);
        Assert.Equal(MessageText.Utf8(BodySample.Outcome), slice.ToArray());
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_request_round_trips_with_the_bytes_of_its_outcome_as_they_were_given()
    {
        byte[] outcome = MessageText.Utf8(OutcomeOfTheExecutor);

        OutpostOutcomeRequest read = Read(OutpostCodec.Write(new OutpostOutcomeRequest(Sample.Command, outcome)));

        Assert.Equal(Sample.Command, read.Command);
        Assert.Equal(outcome, read.Outcome.ToArray());
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(OutcomesThatBreakTheFormOfMessages))]
    public void An_outcome_member_that_breaks_the_form_of_messages_is_refused_with_form(string outcome)
    {
        byte[] body = BodySample.OutcomeRequest.With("outcome", outcome).ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(body));
    }

    // The member rules of §8 are step 3 of §6.3, after the check of the size, which the caller makes between the two.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void An_outcome_that_keeps_the_form_of_messages_and_breaks_a_rule_of_its_members_is_read()
    {
        const string Outcome =
            """{"kind":"expired","code":"not-a-code","result":7,"errorText":"text","messages":[],"messagesTrimmed":true}""";

        OutpostOutcomeRequest request = Read(BodySample.OutcomeRequest.With("outcome", Outcome).ToBytes());

        Assert.Equal(MessageText.Utf8(Outcome), request.Outcome.ToArray());
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("\"9C1E2F3A-4B5C-4D6E-8F70-A1B2C3D4E5F6\"")]
    [InlineData("\"{9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6}\"")]
    [InlineData("\"9c1e2f3a4b5c4d6e8f70a1b2c3d4e5f6\"")]
    [InlineData("null")]
    public void A_command_that_is_no_identity_in_its_form_is_refused_with_form(string command)
    {
        byte[] body = BodySample.OutcomeRequest.With("command", command).ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(body));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_request_that_has_not_exactly_its_two_members_is_refused_with_form()
    {
        byte[] withoutCommand = BodySample.OutcomeRequest.Without("command").ToBytes();
        byte[] withoutOutcome = BodySample.OutcomeRequest.Without("outcome").ToBytes();
        byte[] withAnother = BodySample.OutcomeRequest.With("agent", MessageText.Quote(Sample.AgentText)).ToBytes();

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(withoutCommand));
        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(withoutOutcome));
        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(withAnother));
    }

    private OutpostOutcomeRequest Read(byte[] body)
    {
        byte[] signature = OutpostSignatures.SignRequest(_agentKey, OutpostRequestKindEnum.Outcome, Sample.Agent, body);

        return OutpostCodec.ReadOutcomeRequest(_agentKey, Sample.Agent, body, signature);
    }
}
