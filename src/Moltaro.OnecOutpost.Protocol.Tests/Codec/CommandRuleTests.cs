using System.Security.Cryptography;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The rules of the members of a command (§7 of docs/protocol.md). A verified command of this version that breaks one
// does not run: the reader refuses it with `malformed` under the identity of the command, so that the agent can
// refuse it by that identity.
public sealed class CommandRuleTests : IDisposable
{
    private static readonly DateTimeOffset Issued = new(2026, 11, 20, 10, 15, 0, TimeSpan.Zero);

    private readonly ECDsa _serverKey = Sample.NewKey();

    public static TheoryData<string> GoodCommands =>
    [
        // The Write that §7 shows.
        CommandSample.Text.ToString(),

        // A Read without a caller key, and one with a caller key.
        CommandSample.Text.With("mode", "\"read\"").With("callerKey", "null").ToString(),
        CommandSample.Text.With("mode", "\"read\"").ToString(),

        // No parameters, and parameters of exactly 64 levels.
        CommandSample.Text.With("parameters", "{}").ToString(),
        CommandSample.Text.With("parameters", MessageText.Nested(64)).ToString(),

        // The order of the members carries no meaning, and a reader accepts white space between tokens.
        CommandSample.Text.Reversed().ToString(),
        " \r\n" + CommandSample.Text.ToString().Replace(",", " ,\n\t") + "\n",
    ];

    public static TheoryData<string> BrokenCommands
    {
        get
        {
            MessageText command = CommandSample.Text;
            TheoryData<string> broken = [];

            // A missing member, for each member after the two that name the command.
            foreach (string name in new[]
                {
                    "agent",
                    "base",
                    "mode",
                    "callerKey",
                    "script",
                    "parameters",
                    "issued",
                    "deadline",
                    "skewMs",
                    "outcomeLimit",
                })
            {
                broken.Add(command.Without(name).ToString());
            }

            // An unknown member, and a duplicated one.
            broken.Add(command.With("priority", "1").ToString());
            broken.Add(command.WithAnother("base", "\"accounting\"").ToString());

            // `agent`: an identity in the form of §2.
            broken.Add(command.With("agent", "\"3F2B6C1E-8A4D-4C2F-9B7E-1D0A5E6F7C8B\"").ToString());
            broken.Add(command.With("agent", "null").ToString());

            // `base`: a base key by the rule of names.
            broken.Add(command.With("base", "\"Accounting\"").ToString());
            broken.Add(command.With("base", "\"\"").ToString());
            broken.Add(command.With("base", "null").ToString());

            // `mode`: `read` or `write`.
            broken.Add(command.With("mode", "\"execute\"").ToString());
            broken.Add(command.With("mode", "\"Write\"").ToString());
            broken.Add(command.With("mode", "1").ToString());

            // `callerKey`: a caller key by the rule of names, required for a Write.
            broken.Add(command.With("callerKey", "null").ToString());
            broken.Add(command.With("callerKey", "\"two words\"").ToString());
            broken.Add(command.With("callerKey", "\"\"").ToString());
            broken.Add(command.With("mode", "\"read\"").With("callerKey", "\"two words\"").ToString());

            // `script`: by the rule of scripts, and no string of the protocol holds U+0000 or a lone surrogate.
            broken.Add(command.With("script", "\"\"").ToString());
            broken.Add(command.With("script", "\" \\r\\n\\t \"").ToString());
            broken.Add(command.With("script", "\"Result = \\\"\\u0000\\\";\"").ToString());
            broken.Add(command.With("script", "\"Result = \\\"\\ud83d\\\";\"").ToString());
            broken.Add(command.With("script", "null").ToString());

            // `parameters`: an object of at most 64 levels, without a duplicated name at any depth, the names compared
            // after unescaping, and without U+0000 in a string.
            broken.Add(command.With("parameters", "[]").ToString());
            broken.Add(command.With("parameters", "\"{}\"").ToString());
            broken.Add(command.With("parameters", "null").ToString());
            broken.Add(command.With("parameters", MessageText.Nested(65)).ToString());
            broken.Add(command.With("parameters", "{\"a\":1,\"a\":2}").ToString());
            broken.Add(command.With("parameters", "{\"a\":[{\"b\":1,\"\\u0062\":2}]}").ToString());
            broken.Add(command.With("parameters", "{\"a\":\"\\u0000\"}").ToString());
            broken.Add(command.With("parameters", "{\"\\ude00\":1}").ToString());

            // `issued` and `deadline`: times in the one form of §2.
            broken.Add(command.With("issued", "\"2026-11-20T10:15:00Z\"").ToString());
            broken.Add(command.With("issued", "1795083300").ToString());
            broken.Add(command.With("deadline", "\"2026-11-20T10:15:30.000+00:00\"").ToString());

            // `deadline` is after `issued`.
            broken.Add(command.With("deadline", "\"2026-11-20T10:15:00.000Z\"").ToString());
            broken.Add(command.With("deadline", "\"2026-11-20T10:14:30.000Z\"").ToString());

            // `skewMs` and `outcomeLimit`: integers, without a fraction and without an exponent.
            broken.Add(command.With("skewMs", "\"120000\"").ToString());
            broken.Add(command.With("skewMs", "120000.0").ToString());
            broken.Add(command.With("skewMs", "12e4").ToString());
            broken.Add(command.With("outcomeLimit", "16777216.5").ToString());
            broken.Add(command.With("outcomeLimit", "null").ToString());

            return broken;
        }
    }

    // A member with a range, a value of it, and whether the value is within the range. Every range includes its ends.
    public static TheoryData<string, string, bool> Ranges => new()
    {
        { "skewMs", Milliseconds(OutpostProtocol.MinClockSkew), true },
        { "skewMs", Milliseconds(OutpostProtocol.MinClockSkew, -1), false },
        { "skewMs", Milliseconds(OutpostProtocol.MaxClockSkew), true },
        { "skewMs", Milliseconds(OutpostProtocol.MaxClockSkew, 1), false },
        { "skewMs", "0", false },
        { "skewMs", "-120000", false },

        { "outcomeLimit", (OutpostProtocol.MinOutcomeSizeLimit - 1).ToString(), false },
        { "outcomeLimit", OutpostProtocol.MinOutcomeSizeLimit.ToString(), true },
        { "outcomeLimit", OutpostProtocol.MaxOutcomeSizeLimit.ToString(), true },
        { "outcomeLimit", (OutpostProtocol.MaxOutcomeSizeLimit + 1).ToString(), false },

        // The take deadline is after `issued` by a duration in the range of TakeTimeout.
        { "deadline", MessageText.Time(Issued + OutpostProtocol.MinTakeTimeout), true },
        { "deadline", MessageText.Time(Issued + OutpostProtocol.MinTakeTimeout - TimeSpan.FromMilliseconds(1)), false },
        { "deadline", MessageText.Time(Issued + OutpostProtocol.MaxTakeTimeout), true },
        { "deadline", MessageText.Time(Issued + OutpostProtocol.MaxTakeTimeout + TimeSpan.FromMilliseconds(1)), false },
    };

    public void Dispose() => _serverKey.Dispose();

    [Fact]
    public void The_command_that_the_protocol_shows_is_read_into_its_members()
    {
        OutpostCommandMessage command = Read(CommandSample.Text.ToString());

        Assert.Equal(1, command.Protocol);
        Assert.Equal(Sample.Command, command.Id);
        Assert.Equal(Sample.Agent, command.Agent);
        Assert.Equal("accounting", command.Base);
        Assert.Equal(OutpostModeEnum.Write, command.Mode);
        Assert.Equal("invoice-2026-11-20-17", command.CallerKey);
        Assert.Equal("Result = Parameters[\"date\"];", command.Script);
        Assert.Equal(JsonValueKind.Object, command.Parameters.ValueKind);
        Assert.Equal("2026-11-20", command.Parameters.GetProperty("date").GetString());
        Assert.Equal(Issued, command.Issued);
        Assert.Equal(Issued.AddSeconds(30), command.Deadline);
        Assert.Equal(TimeSpan.FromMinutes(2), command.Skew);
        Assert.Equal(16 * 1024 * 1024, command.OutcomeLimit);
    }

    [Fact]
    public void A_Read_without_a_caller_key_is_read_with_none()
    {
        OutpostCommandMessage command = Read(
            CommandSample.Text.With("mode", "\"read\"").With("callerKey", "null").ToString());

        Assert.Equal(OutpostModeEnum.Read, command.Mode);
        Assert.Null(command.CallerKey);
    }

    [Theory]
    [MemberData(nameof(GoodCommands))]
    public void A_command_that_keeps_the_rules_of_its_members_is_read(string text)
    {
        OutpostCommandMessage command = Read(text);

        Assert.Equal(Sample.Command, command.Id);
    }

    [Theory]
    [MemberData(nameof(BrokenCommands))]
    public void A_command_that_breaks_a_rule_of_its_members_is_refused_with_malformed_under_its_identity(string text)
    {
        AssertMalformed(text);
    }

    [Theory]
    [MemberData(nameof(Ranges))]
    public void A_member_is_accepted_at_the_ends_of_its_range_and_refused_one_step_beyond_them(
        string member,
        string value,
        bool within)
    {
        string text = CommandSample.Text.With(member, value).ToString();

        if (within)
        {
            Assert.Equal(Sample.Command, Read(text).Id);
        }
        else
        {
            AssertMalformed(text);
        }
    }

    [Fact]
    public void A_command_round_trips()
    {
        using JsonDocument parameters = JsonDocument.Parse("{\"date\":\"2026-11-20\",\"rows\":[1,2.5,null,true,{\"\u00e9\":\"\\\"\"}]}");

        OutpostCommandMessage written = new(
            OutpostProtocol.Version,
            Sample.Command,
            Sample.Agent,
            "accounting",
            OutpostModeEnum.Write,
            "invoice-2026-11-20-17",
            "Result = Parameters[\"date\"];\r\n// \u00e9 \U0001F600 \u0001",
            parameters.RootElement,
            Issued.AddMilliseconds(123),
            Issued.AddSeconds(30),
            TimeSpan.FromMinutes(2),
            16 * 1024 * 1024);

        byte[] bytes = OutpostCodec.Write(written);
        OutpostCommandMessage read = OutpostCodec.ReadCommand(
            _serverKey,
            new OutpostSignedCommand(bytes, OutpostSignatures.SignCommand(_serverKey, bytes)));

        Assert.Equal(written.Protocol, read.Protocol);
        Assert.Equal(written.Id, read.Id);
        Assert.Equal(written.Agent, read.Agent);
        Assert.Equal(written.Base, read.Base);
        Assert.Equal(written.Mode, read.Mode);
        Assert.Equal(written.CallerKey, read.CallerKey);
        Assert.Equal(written.Script, read.Script);
        Assert.True(JsonElement.DeepEquals(written.Parameters, read.Parameters));
        Assert.Equal(written.Issued, read.Issued);
        Assert.Equal(written.Deadline, read.Deadline);
        Assert.Equal(written.Skew, read.Skew);
        Assert.Equal(written.OutcomeLimit, read.OutcomeLimit);
    }

    private static string Milliseconds(TimeSpan duration, int step = 0)
    {
        return ((long)duration.TotalMilliseconds + step).ToString();
    }

    private OutpostCommandMessage Read(string text)
    {
        byte[] bytes = MessageText.Utf8(text);

        return OutpostCodec.ReadCommand(
            _serverKey,
            new OutpostSignedCommand(bytes, OutpostSignatures.SignCommand(_serverKey, bytes)));
    }

    private void AssertMalformed(string text)
    {
        OutpostProtocolException refusal = ProtocolAssert.Refused(OutpostOutcomeCodes.Malformed, () => Read(text));

        Assert.Equal(Sample.Command, refusal.CommandId);
    }
}
