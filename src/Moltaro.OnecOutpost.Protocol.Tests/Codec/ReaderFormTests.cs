using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// What every reader does by §2 of docs/protocol.md, stated once, on the answer to a poll: it accepts insignificant
// white space, every escape and any order of the members, and refuses with `form` a message that has not exactly its
// members, each once and of its type, in the forms of §2. The rules of the members of each message have their own
// tests.
public sealed class ReaderFormTests : IDisposable
{
    private static readonly byte[] Request = BodySample.Poll.ToBytes();

    private readonly ECDsa _serverKey = Sample.NewKey();

    public static TheoryData<string> BrokenBodies
    {
        get
        {
            MessageText answer = BodySample.PollAnswer;
            string text = answer.ToString();
            TheoryData<string> broken = [];

            // A missing member, at the top and inside an object.
            foreach (string name in new[] { "time", "newest", "pollTimeoutMs", "leaseGraceMs", "bases", "commands" })
            {
                broken.Add(answer.Without(name).ToString());
            }

            broken.Add(answer.With("bases", """{"held":["accounting"]}""").ToString());
            broken.Add(answer.With("bases", """{"claimed":[]}""").ToString());

            // An unknown member, at the top and inside an object.
            broken.Add(answer.With("extra", "1").ToString());
            broken.Add(answer.With("bases", """{"held":["accounting"],"claimed":[],"released":[]}""").ToString());

            // A duplicated member, at the top and inside an object, the names compared after unescaping.
            broken.Add(answer.WithAnother("time", "\"2026-11-20T10:15:01.456Z\"").ToString());
            broken.Add(answer.WithAnother("t\\u0069me", "\"2026-11-20T10:15:01.456Z\"").ToString());
            broken.Add(answer.With("bases", """{"held":["accounting"],"held":["accounting"],"claimed":[]}""").ToString());
            broken.Add(answer.With("bases", """{"held":["accounting"],"claimed":[],"cl\u0061imed":[]}""").ToString());

            // `null` where the document does not allow it.
            broken.Add(answer.With("time", "null").ToString());
            broken.Add(answer.With("pollTimeoutMs", "null").ToString());
            broken.Add(answer.With("bases", "null").ToString());
            broken.Add(answer.With("commands", "null").ToString());
            broken.Add(answer.With("bases", """{"held":null,"claimed":[]}""").ToString());
            broken.Add(answer.With("bases", """{"held":[null],"claimed":[]}""").ToString());

            // A value of the wrong type.
            broken.Add(answer.With("time", "1795083301456").ToString());
            broken.Add(answer.With("newest", "\"1\"").ToString());
            broken.Add(answer.With("newest", "true").ToString());
            broken.Add(answer.With("pollTimeoutMs", "\"25000\"").ToString());
            broken.Add(answer.With("leaseGraceMs", "false").ToString());
            broken.Add(answer.With("bases", "[]").ToString());
            broken.Add(answer.With("bases", """{"held":"accounting","claimed":[]}""").ToString());
            broken.Add(answer.With("bases", """{"held":[1],"claimed":[]}""").ToString());
            broken.Add(answer.With("commands", "{}").ToString());
            broken.Add(answer.With("commands", "[\"command\"]").ToString());

            // An integer with a fraction or an exponent.
            broken.Add(answer.With("pollTimeoutMs", "25000.0").ToString());
            broken.Add(answer.With("pollTimeoutMs", "25e3").ToString());
            broken.Add(answer.With("pollTimeoutMs", "2.5e4").ToString());
            broken.Add(answer.With("leaseGraceMs", "10000E0").ToString());
            broken.Add(answer.With("newest", "7.0").ToString());

            // A time in any form but yyyy-MM-ddTHH:mm:ss.fffZ, and a time that no calendar holds.
            broken.Add(answer.With("time", "\"2026-11-20T10:15:01Z\"").ToString());
            broken.Add(answer.With("time", "\"2026-11-20T10:15:01.45Z\"").ToString());
            broken.Add(answer.With("time", "\"2026-11-20T10:15:01.4567Z\"").ToString());
            broken.Add(answer.With("time", "\"2026-11-20T10:15:01.456\"").ToString());
            broken.Add(answer.With("time", "\"2026-11-20T10:15:01.456+00:00\"").ToString());
            broken.Add(answer.With("time", "\"2026-11-20T12:15:01.456+02:00\"").ToString());
            broken.Add(answer.With("time", "\"2026-11-20 10:15:01.456Z\"").ToString());
            broken.Add(answer.With("time", "\"2026-11-20t10:15:01.456z\"").ToString());
            broken.Add(answer.With("time", "\"2026-11-20T10:15:01,456Z\"").ToString());
            broken.Add(answer.With("time", "\" 2026-11-20T10:15:01.456Z\"").ToString());
            broken.Add(answer.With("time", "\"2026-13-20T10:15:01.456Z\"").ToString());
            broken.Add(answer.With("time", "\"2026-11-20T24:15:01.456Z\"").ToString());

            // A text that is not one JSON object.
            broken.Add(string.Empty);
            broken.Add("null");
            broken.Add("[]");
            broken.Add("\"text\"");
            broken.Add(text[..^1]);
            broken.Add(text + "x");
            broken.Add(text + text);
            broken.Add(text.Replace("25000", "025000"));

            return broken;
        }
    }

    public void Dispose() => _serverKey.Dispose();

    [Fact]
    public void A_body_in_the_form_of_the_writers_is_read()
    {
        OutpostPollAnswer answer = Read(BodySample.PollAnswer.ToString());

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 15, 1, 456, TimeSpan.Zero), answer.Time);
        Assert.Equal(["accounting"], answer.Held);
    }

    [Fact]
    public void White_space_between_tokens_is_accepted()
    {
        string text = BodySample.PollAnswer.ToString()
            .Replace(",", " ,\r\n\t")
            .Replace("{", "{ ")
            .Replace("}", "\t}")
            .Replace("[", "[\n")
            .Replace("]", " ]")
            .Replace("\":", "\" : ");

        OutpostPollAnswer answer = Read("\r\n\t " + text + " \n");

        Assert.Equal(["accounting"], answer.Held);
        Assert.Equal(TimeSpan.FromSeconds(25), answer.PollTimeout);
    }

    [Fact]
    public void The_order_of_the_members_carries_no_meaning()
    {
        OutpostPollAnswer answer = Read(BodySample.PollAnswer.Reversed().ToString());

        Assert.Equal(["accounting"], answer.Held);
        Assert.Equal(TimeSpan.FromSeconds(10), answer.LeaseGrace);
    }

    [Fact]
    public void An_escape_in_a_name_or_in_a_value_is_read_as_its_character()
    {
        string text = BodySample.PollAnswer
            .Without("time")
            .With("t\\u0069me", "\"2026-11-20T10:15:01.456Z\"")
            .With("bases", """{"held":["\u0061ccounting"],"claimed":[]}""")
            .ToString();

        OutpostPollAnswer answer = Read(text);

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 15, 1, 456, TimeSpan.Zero), answer.Time);
        Assert.Equal(["accounting"], answer.Held);
    }

    // The outcome object is the one message with a free text, so every escape of RFC 8259 is shown on it.
    [Fact]
    public void Every_escape_of_JSON_is_accepted()
    {
        const string Text =
            """{"kind":"failed","code":"script-error","result":null,"errorText":"\"\\\/\b\f\n\r\t\u00e9\u00E9\ud83d\ude00","messages":[],"messagesTrimmed":false}""";

        OutpostOutcomeMessage outcome = OutpostCodec.ReadOutcome(OutpostModeEnum.Read, MessageText.Utf8(Text));

        Assert.Equal("\"\\/\b\f\n\r\t\u00e9\u00e9\U0001F600", outcome.ErrorText);
    }

    [Theory]
    [MemberData(nameof(BrokenBodies))]
    public void A_verified_body_that_breaks_the_form_of_messages_is_refused_with_form(string text)
    {
        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(text));
    }

    [Fact]
    public void A_body_that_begins_with_a_byte_order_mark_is_refused_with_form()
    {
        byte[] body = [0xEF, 0xBB, 0xBF, .. BodySample.PollAnswer.ToBytes()];

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(body));
    }

    private OutpostPollAnswer Read(string text)
    {
        return Read(MessageText.Utf8(text));
    }

    private OutpostPollAnswer Read(byte[] body)
    {
        byte[] signature = OutpostSignatures.SignAnswer(
            _serverKey,
            OutpostRequestKindEnum.Poll,
            Sample.Agent,
            200,
            Request,
            body);

        return OutpostCodec.ReadPollAnswer(_serverKey, Sample.Agent, 2, Request, body, signature);
    }
}
