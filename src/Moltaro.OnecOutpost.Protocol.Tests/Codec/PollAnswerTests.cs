using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The body of the answer to a `poll` (§6.2 of docs/protocol.md) and its own rules: it holds no more commands than the
// sum of `free` in its poll, and none when `newest` is not null. A verified answer that breaks a rule is refused with
// `form`, and the agent counts it as lost. The reader verifies and reads no command: each is checked by §7 on its own.
public sealed class PollAnswerTests : IDisposable
{
    private static readonly byte[] Request = BodySample.Poll.ToBytes();

    private readonly ECDsa _serverKey = Sample.NewKey();

    // A member with a range, a value of it, and whether the value is within the range. Every range includes its ends.
    public static TheoryData<string, string, bool> Ranges => new()
    {
        { "newest", "0", false },
        { "newest", "1", true },
        { "newest", OutpostProtocol.MaxPollNumber.ToString(), true },
        { "newest", (OutpostProtocol.MaxPollNumber + 1).ToString(), false },

        { "pollTimeoutMs", MessageText.Milliseconds(OutpostProtocol.MinPollTimeout, -1), false },
        { "pollTimeoutMs", MessageText.Milliseconds(OutpostProtocol.MinPollTimeout), true },
        { "pollTimeoutMs", MessageText.Milliseconds(OutpostProtocol.MaxPollTimeout), true },
        { "pollTimeoutMs", MessageText.Milliseconds(OutpostProtocol.MaxPollTimeout, 1), false },

        { "leaseGraceMs", MessageText.Milliseconds(OutpostProtocol.MinLeaseGrace, -1), false },
        { "leaseGraceMs", MessageText.Milliseconds(OutpostProtocol.MinLeaseGrace), true },
        { "leaseGraceMs", MessageText.Milliseconds(OutpostProtocol.MaxLeaseGrace), true },
        { "leaseGraceMs", MessageText.Milliseconds(OutpostProtocol.MaxLeaseGrace, 1), false },
    };

    public void Dispose() => _serverKey.Dispose();

    [Fact]
    public void An_answer_with_a_command_is_read_into_its_members()
    {
        byte[] bytes = CommandSample.Text.ToBytes();
        byte[] signature = RandomNumberGenerator.GetBytes(64);
        string text = BodySample.PollAnswer
            .With("bases", """{"held":["accounting"],"claimed":["warehouse"]}""")
            .With("commands", MessageText.Array([Command(bytes, signature)]))
            .ToString();

        OutpostPollAnswer answer = Read(text, totalFree: 2);

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 15, 1, 456, TimeSpan.Zero), answer.Time);
        Assert.Null(answer.Newest);
        Assert.Equal(TimeSpan.FromSeconds(25), answer.PollTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), answer.LeaseGrace);
        Assert.Equal("accounting", Assert.Single(answer.Held));
        Assert.Equal("warehouse", Assert.Single(answer.Claimed));

        OutpostSignedCommand command = Assert.Single(answer.Commands);
        Assert.Equal(bytes, command.Bytes);
        Assert.Equal(signature, command.Signature);
    }

    [Fact]
    public void The_answer_to_an_older_poll_is_read_with_the_newest_number_and_no_commands()
    {
        OutpostPollAnswer answer = Read(BodySample.PollAnswer.With("newest", "639307665001230001").ToString(), totalFree: 2);

        Assert.Equal(639307665001230001, answer.Newest);
        Assert.Empty(answer.Commands);
    }

    // Nothing in the bytes of a command is trusted before its own signature is verified, and that is not this reader's.
    [Fact]
    public void The_reader_of_an_answer_verifies_and_reads_no_command()
    {
        byte[] bytes = MessageText.Utf8("not a command");
        string text = BodySample.PollAnswer
            .With("commands", MessageText.Array([Command(bytes, new byte[64])]))
            .ToString();

        OutpostPollAnswer answer = Read(text, totalFree: 2);

        Assert.Equal(bytes, Assert.Single(answer.Commands).Bytes);
    }

    [Fact]
    public void An_answer_round_trips()
    {
        OutpostPollAnswer written = new(
            new DateTimeOffset(2026, 11, 20, 10, 15, 1, 456, TimeSpan.Zero),
            null,
            TimeSpan.FromSeconds(25),
            TimeSpan.FromSeconds(10),
            ["accounting", "warehouse"],
            ["archive"],
            [
                new OutpostSignedCommand(CommandSample.Text.ToBytes(), RandomNumberGenerator.GetBytes(64)),
                new OutpostSignedCommand(RandomNumberGenerator.GetBytes(100), RandomNumberGenerator.GetBytes(64)),
            ]);

        byte[] body = OutpostCodec.Write(written);
        OutpostPollAnswer read = OutpostCodec.ReadPollAnswer(_serverKey, Sample.Agent, 2, Request, body, Sign(body));

        Assert.Equal(written.Time, read.Time);
        Assert.Null(read.Newest);
        Assert.Equal(written.PollTimeout, read.PollTimeout);
        Assert.Equal(written.LeaseGrace, read.LeaseGrace);
        Assert.Equal<string>(written.Held, read.Held);
        Assert.Equal<string>(written.Claimed, read.Claimed);
        Assert.Equal(2, read.Commands.Count);
        Assert.Equal(written.Commands[0].Bytes, read.Commands[0].Bytes);
        Assert.Equal(written.Commands[0].Signature, read.Commands[0].Signature);
        Assert.Equal(written.Commands[1].Bytes, read.Commands[1].Bytes);
        Assert.Equal(written.Commands[1].Signature, read.Commands[1].Signature);
    }

    [Fact]
    public void The_answer_to_an_older_poll_round_trips()
    {
        OutpostPollAnswer written = new(
            new DateTimeOffset(2026, 11, 20, 10, 15, 1, 456, TimeSpan.Zero),
            OutpostProtocol.MaxPollNumber,
            TimeSpan.FromSeconds(25),
            TimeSpan.FromSeconds(10),
            [],
            [],
            []);

        byte[] body = OutpostCodec.Write(written);
        OutpostPollAnswer read = OutpostCodec.ReadPollAnswer(_serverKey, Sample.Agent, 0, Request, body, Sign(body));

        Assert.Equal(OutpostProtocol.MaxPollNumber, read.Newest);
        Assert.Empty(read.Held);
        Assert.Empty(read.Claimed);
        Assert.Empty(read.Commands);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(2, 2, true)]
    [InlineData(3, 2, false)]
    [InlineData(1, 0, false)]
    public void An_answer_holds_no_more_commands_than_the_sum_of_free_in_its_poll(int commands, int totalFree, bool within)
    {
        string text = BodySample.PollAnswer.With("commands", Commands(commands)).ToString();

        AssertReadOnlyIf(within, text, totalFree);
    }

    [Fact]
    public void An_answer_with_commands_and_a_newest_is_refused_with_form()
    {
        string text = BodySample.PollAnswer.With("newest", "7").With("commands", Commands(1)).ToString();

        AssertReadOnlyIf(false, text, totalFree: 2);
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void A_member_is_accepted_at_the_ends_of_its_range_and_refused_one_step_beyond_them(
        string member,
        string value,
        bool within)
    {
        AssertReadOnlyIf(within, BodySample.PollAnswer.With(member, value).ToString(), totalFree: 2);
    }

    [Theory]

    // A signature of 63 bytes and one of 65; bytes and a signature that are not canonical base64url; a command that
    // has not exactly its two members.
    [InlineData("""{"bytes":"e30","signature":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"bytes":"e30","signature":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"bytes":"e30=","signature":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"bytes":"e31","signature":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"bytes":"e30","signature":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB"}""")]
    [InlineData("""{"bytes":"e30"}""")]
    [InlineData("""{"signature":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"bytes":null,"signature":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"bytes":"e30","signature":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","id":1}""")]
    public void A_command_that_has_not_its_bytes_and_a_signature_of_64_bytes_in_their_form_is_refused_with_form(
        string command)
    {
        string text = BodySample.PollAnswer.With("commands", MessageText.Array([command])).ToString();

        AssertReadOnlyIf(false, text, totalFree: 2);
    }

    private static string Command(byte[] bytes, byte[] signature)
    {
        return $"{{\"bytes\":\"{MessageText.Base64Url(bytes)}\",\"signature\":\"{MessageText.Base64Url(signature)}\"}}";
    }

    private static string Commands(int count)
    {
        return MessageText.Array(Enumerable.Repeat(Command(CommandSample.Text.ToBytes(), new byte[64]), count));
    }

    private void AssertReadOnlyIf(bool within, string text, int totalFree)
    {
        if (within)
        {
            Read(text, totalFree);
        }
        else
        {
            ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(text, totalFree));
        }
    }

    private OutpostPollAnswer Read(string text, int totalFree)
    {
        byte[] body = MessageText.Utf8(text);

        return OutpostCodec.ReadPollAnswer(_serverKey, Sample.Agent, totalFree, Request, body, Sign(body));
    }

    private byte[] Sign(byte[] body)
    {
        return OutpostSignatures.SignAnswer(_serverKey, OutpostRequestKindEnum.Poll, Sample.Agent, 200, Request, body);
    }
}
