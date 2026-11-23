using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The body of a `poll` (§6.2 of docs/protocol.md) and the rules of its members: the range of the poll number and of
// the poll timeout, the bases with their modes, and the list of received commands with the room it leaves for the
// free workers. A verified poll that breaks one is refused with `form`.
public sealed class PollRequestTests : IDisposable
{
    private readonly ECDsa _agentKey = Sample.NewKey();

    // A member with a range, a value of it, and whether the value is within the range. Every range includes its ends.
    public static TheoryData<string, string, bool> Ranges => new()
    {
        { "number", "0", false },
        { "number", "1", true },
        { "number", OutpostProtocol.MaxPollNumber.ToString(), true },
        { "number", (OutpostProtocol.MaxPollNumber + 1).ToString(), false },
        { "number", "-1", false },
        { "number", "9223372036854775808", false },

        { "pollTimeoutMs", MessageText.Milliseconds(OutpostProtocol.MinPollTimeout, -1), false },
        { "pollTimeoutMs", MessageText.Milliseconds(OutpostProtocol.MinPollTimeout), true },
        { "pollTimeoutMs", MessageText.Milliseconds(OutpostProtocol.MaxPollTimeout), true },
        { "pollTimeoutMs", MessageText.Milliseconds(OutpostProtocol.MaxPollTimeout, 1), false },
    };

    // A mode of a base, the member of it with a range, a value of it, and whether the value is within the range.
    public static TheoryData<string, string, string, bool> ModeRanges
    {
        get
        {
            TheoryData<string, string, string, bool> ranges = [];

            foreach (string mode in new[] { "read", "write" })
            {
                ranges.Add(mode, "free", "-1", false);
                ranges.Add(mode, "free", "0", true);
                ranges.Add(mode, "free", OutpostProtocol.MaxWorkers.ToString(), true);
                ranges.Add(mode, "free", (OutpostProtocol.MaxWorkers + 1).ToString(), false);

                ranges.Add(mode, "runLimitMs", MessageText.Milliseconds(OutpostProtocol.MinRunLimit, -1), false);
                ranges.Add(mode, "runLimitMs", MessageText.Milliseconds(OutpostProtocol.MinRunLimit), true);
                ranges.Add(mode, "runLimitMs", MessageText.Milliseconds(OutpostProtocol.MaxRunLimit), true);
                ranges.Add(mode, "runLimitMs", MessageText.Milliseconds(OutpostProtocol.MaxRunLimit, 1), false);
            }

            return ranges;
        }
    }

    public void Dispose() => _agentKey.Dispose();

    [Fact]
    public void The_poll_that_the_protocol_shows_is_read_into_its_members()
    {
        OutpostPollRequest poll = Read(BodySample.Poll.ToString());

        Assert.Equal(639307665001230000, poll.Number);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 15, 0, 123, TimeSpan.Zero), poll.Time);
        Assert.Equal("0.1.0", poll.AgentVersion);
        Assert.Equal(TimeSpan.FromSeconds(25), poll.PollTimeout);
        Assert.Equal(2, poll.Bases.Count);
        Assert.Equal(
            new OutpostPollBase(
                "accounting",
                new OutpostPollMode(1, TimeSpan.FromMinutes(10)),
                new OutpostPollMode(0, TimeSpan.FromMinutes(10))),
            poll.Bases[0]);
        Assert.Equal(Sample.Command, Assert.Single(poll.Received));
    }

    [Fact]
    public void A_base_with_a_null_write_is_read_as_a_base_without_writing()
    {
        OutpostPollRequest poll = Read(BodySample.Poll.ToString());

        Assert.Equal("warehouse", poll.Bases[1].Key);
        Assert.Equal(new OutpostPollMode(1, TimeSpan.FromMinutes(10)), poll.Bases[1].Read);
        Assert.Null(poll.Bases[1].Write);
    }

    [Fact]
    public void A_poll_round_trips()
    {
        OutpostPollRequest written = new(
            OutpostProtocol.MaxPollNumber,
            new DateTimeOffset(2026, 11, 20, 10, 15, 0, 123, TimeSpan.Zero),
            "0.1.0-rc.1+build-7",
            TimeSpan.FromSeconds(25),
            [
                new OutpostPollBase(
                    "accounting",
                    new OutpostPollMode(2, TimeSpan.FromMinutes(10)),
                    new OutpostPollMode(0, TimeSpan.FromHours(1))),
                new OutpostPollBase("warehouse_2.test-base", new OutpostPollMode(1, TimeSpan.FromSeconds(1)), null),
            ],
            [Sample.Command, Sample.OtherCommand]);

        byte[] body = OutpostCodec.Write(written);
        OutpostPollRequest read = OutpostCodec.ReadPollRequest(_agentKey, Sample.Agent, body, Sign(body));

        Assert.Equal(written.Number, read.Number);
        Assert.Equal(written.Time, read.Time);
        Assert.Equal(written.AgentVersion, read.AgentVersion);
        Assert.Equal(written.PollTimeout, read.PollTimeout);
        Assert.Equal<OutpostPollBase>(written.Bases, read.Bases);
        Assert.Equal<Guid>(written.Received, read.Received);
    }

    [Fact]
    public void A_poll_with_no_bases_and_nothing_received_is_read()
    {
        OutpostPollRequest poll = Read(BodySample.Poll.With("bases", "[]").With("received", "[]").ToString());

        Assert.Empty(poll.Bases);
        Assert.Empty(poll.Received);
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void A_member_is_accepted_at_the_ends_of_its_range_and_refused_one_step_beyond_them(
        string member,
        string value,
        bool within)
    {
        AssertReadOnlyIf(within, BodySample.Poll.With(member, value).ToString());
    }

    [Theory]
    [MemberData(nameof(ModeRanges))]
    public void A_member_of_a_mode_is_accepted_at_the_ends_of_its_range_and_refused_one_step_beyond_them(
        string mode,
        string member,
        string value,
        bool within)
    {
        string changed = member == "free" ? Mode(free: value) : Mode(runLimitMs: value);
        string read = mode == "read" ? changed : Mode();
        string write = mode == "write" ? changed : Mode();

        AssertReadOnlyIf(within, PollWith([Base("accounting", read, write)], received: 0));
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void A_poll_declares_at_most_256_bases(int count, bool within)
    {
        IEnumerable<string> bases = Enumerable.Range(0, count).Select(index => Base("base-" + index, Mode(), "null"));

        AssertReadOnlyIf(within, PollWith(bases, received: 0));
    }

    [Theory]

    // A key that repeats, and keys that break the rule of names.
    [InlineData("accounting", "accounting")]
    [InlineData("accounting", "Warehouse")]
    [InlineData("accounting", "")]
    [InlineData("accounting", "-warehouse")]
    public void Bases_whose_keys_repeat_or_break_the_rule_of_names_are_refused_with_form(string first, string second)
    {
        AssertReadOnlyIf(false, PollWith([Base(first, Mode(), "null"), Base(second, Mode(), "null")], received: 0));
    }

    [Theory]

    // `read` is an object, and `write` an object or null; each mode has exactly its two members.
    [InlineData("""{"key":"accounting","read":null,"write":null}""")]
    [InlineData("""{"key":"accounting","read":{"free":1,"runLimitMs":600000}}""")]
    [InlineData("""{"key":"accounting","write":{"free":1,"runLimitMs":600000}}""")]
    [InlineData("""{"read":{"free":1,"runLimitMs":600000},"write":null}""")]
    [InlineData("""{"key":"accounting","read":{"free":1},"write":null}""")]
    [InlineData("""{"key":"accounting","read":{"free":1,"runLimitMs":600000,"workers":2},"write":null}""")]
    [InlineData("""{"key":"accounting","read":{"free":1.0,"runLimitMs":600000},"write":null}""")]
    [InlineData("""{"key":"accounting","read":{"free":1,"runLimitMs":600000},"write":false}""")]
    [InlineData("""{"key":null,"read":{"free":1,"runLimitMs":600000},"write":null}""")]
    [InlineData("\"accounting\"")]
    public void A_base_that_has_not_exactly_its_members_is_refused_with_form(string @base)
    {
        AssertReadOnlyIf(false, PollWith([@base], received: 0));
    }

    // The sum of `free` over all modes is at most the room left in the list, so that no answer can overfill it.
    [Theory]
    [InlineData(10_000, 0, 0, true)]
    [InlineData(10_001, 0, 0, false)]
    [InlineData(9_999, 1, 0, true)]
    [InlineData(9_999, 0, 1, true)]
    [InlineData(9_999, 1, 1, false)]
    [InlineData(9_990, 8, 2, true)]
    [InlineData(9_990, 8, 3, false)]
    public void The_list_of_received_commands_holds_at_most_10000_entries_and_leaves_room_for_every_free_worker(
        int received,
        int freeOfRead,
        int freeOfWrite,
        bool within)
    {
        string @base = Base("accounting", Mode(free: freeOfRead.ToString()), Mode(free: freeOfWrite.ToString()));

        AssertReadOnlyIf(within, PollWith([@base], received));
    }

    [Fact]
    public void The_free_workers_of_every_base_count_against_the_room_left_in_the_list()
    {
        string first = Base("accounting", Mode(free: "1"), "null");
        string second = Base("warehouse", Mode(free: "1"), "null");

        AssertReadOnlyIf(true, PollWith([first, second], received: 9_998));
        AssertReadOnlyIf(false, PollWith([first, second], received: 9_999));
    }

    [Fact]
    public void A_received_command_listed_twice_is_refused_with_form()
    {
        string received = MessageText.Array([MessageText.Quote(Sample.CommandText), MessageText.Quote(Sample.CommandText)]);

        AssertReadOnlyIf(false, BodySample.Poll.With("received", received).ToString());
    }

    [Theory]
    [InlineData("\"9C1E2F3A-4B5C-4D6E-8F70-A1B2C3D4E5F6\"")]
    [InlineData("\"{9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6}\"")]
    [InlineData("null")]
    [InlineData("17")]
    public void A_received_command_that_is_no_identity_in_its_form_is_refused_with_form(string entry)
    {
        AssertReadOnlyIf(false, BodySample.Poll.With("received", MessageText.Array([entry])).ToString());
    }

    // An agent version is 1 to 64 characters, each one of A–Z a–z 0–9 . - + (§2).
    [Theory]
    [InlineData("1", true)]
    [InlineData("AZaz09.-+", true)]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123", true)]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234", false)]
    [InlineData("", false)]
    [InlineData("0.1 0", false)]
    [InlineData("0.1.0_rc", false)]
    [InlineData("0.1.0/rc", false)]
    [InlineData("0.1.0-\u00e9", false)]
    public void An_agent_version_is_1_to_64_characters_of_its_alphabet(string version, bool within)
    {
        AssertReadOnlyIf(within, BodySample.Poll.With("agentVersion", MessageText.Quote(version)).ToString());
    }

    private static string Mode(string free = "0", string runLimitMs = "600000")
    {
        return $"{{\"free\":{free},\"runLimitMs\":{runLimitMs}}}";
    }

    private static string Base(string key, string read, string write)
    {
        return $"{{\"key\":\"{key}\",\"read\":{read},\"write\":{write}}}";
    }

    // A poll with the bases and with the number of received commands, each of another identity.
    private static string PollWith(IEnumerable<string> bases, int received)
    {
        IEnumerable<string> identities = Enumerable
            .Range(1, received)
            .Select(index => MessageText.Quote($"{index:x8}-0000-4000-8000-000000000000"));

        return BodySample.Poll
            .With("bases", MessageText.Array(bases))
            .With("received", MessageText.Array(identities))
            .ToString();
    }

    private void AssertReadOnlyIf(bool within, string text)
    {
        if (within)
        {
            Read(text);
        }
        else
        {
            ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(text));
        }
    }

    private OutpostPollRequest Read(string text)
    {
        byte[] body = MessageText.Utf8(text);

        return OutpostCodec.ReadPollRequest(_agentKey, Sample.Agent, body, Sign(body));
    }

    private byte[] Sign(byte[] body)
    {
        return OutpostSignatures.SignRequest(_agentKey, OutpostRequestKindEnum.Poll, Sample.Agent, body);
    }
}
