using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// What every writer of the library does by §2 of docs/protocol.md: UTF-8 without a byte order mark, no white space
// between tokens, only the escapes of §2, every member present, and times and durations truncated to milliseconds.
// The order of the members carries no meaning, so these tests look for a member with its value, never at the whole
// body.
public sealed class WriterFormTests
{
    private static readonly DateTimeOffset Time = new(2026, 11, 20, 10, 15, 0, 123, TimeSpan.Zero);

    [Fact]
    public void A_writer_escapes_the_quote_the_backslash_and_the_control_characters_and_nothing_else()
    {
        // The quote, the backslash, the five control characters with a short escape, the first and the last of the
        // others, and then characters that many writers escape and this one does not.
        const string Written = "\"\\\b\t\n\f\r\u0001\u001f/<>&'+\u007f\u00e9\u2028\U0001F600";
        const string Expected = """
            "errorText":"\"\\\b\t\n\f\r\u0001\u001F/<>&'+
            """
            + "\u007f\u00e9\u2028\U0001F600\"";

        OutpostOutcomeMessage outcome = new(
            OutpostAgentOutcomeKindEnum.Failed,
            OutpostOutcomeCodes.ScriptError,
            null,
            Written,
            [],
            false);

        Assert.Contains(Expected, Text(OutpostCodec.Write(outcome)));
    }

    [Fact]
    public void A_writer_writes_no_white_space_between_tokens_and_no_byte_order_mark()
    {
        byte[] body = OutpostCodec.Write(Poll());

        Assert.Equal((byte)'{', body[0]);
        Assert.Equal((byte)'}', body[^1]);
        Assert.DoesNotContain(body, value => value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n');
    }

    [Fact]
    public void A_writer_writes_every_member_a_null_one_included()
    {
        OutpostOutcomeMessage outcome = new(
            OutpostAgentOutcomeKindEnum.Refused,
            OutpostOutcomeCodes.NotServed,
            null,
            null,
            [],
            false);

        string text = Text(OutpostCodec.Write(outcome));

        Assert.Contains("\"kind\":\"refused\"", text);
        Assert.Contains("\"code\":\"not-served\"", text);
        Assert.Contains("\"result\":null", text);
        Assert.Contains("\"errorText\":null", text);
        Assert.Contains("\"messages\":[]", text);
        Assert.Contains("\"messagesTrimmed\":false", text);
    }

    [Fact]
    public void A_base_without_writing_is_written_with_a_null_write()
    {
        string text = Text(OutpostCodec.Write(Poll()));

        Assert.Contains("\"key\":\"warehouse\"", text);
        Assert.Contains("\"write\":null", text);
    }

    [Fact]
    public void A_time_is_written_in_UTC_and_truncated_to_milliseconds()
    {
        // 10:15:00.1239999 in UTC, given two hours east of it.
        DateTimeOffset time = Time.AddTicks(9999).ToOffset(TimeSpan.FromHours(2));

        string text = Text(OutpostCodec.Write(new OutpostRefusal(time, OutpostProtocolCodes.Form)));

        Assert.Contains("\"time\":\"2026-11-20T10:15:00.123Z\"", text);
    }

    [Fact]
    public void A_duration_is_written_as_whole_milliseconds_and_truncated_to_them()
    {
        OutpostPairAnswer answer = new(
            Time,
            Sample.Agent,
            Sample.PublicKey,
            Sample.PublicKey,
            TimeSpan.FromMilliseconds(25000) + TimeSpan.FromTicks(9999),
            TimeSpan.FromMilliseconds(10000) + TimeSpan.FromTicks(1));

        using JsonDocument written = JsonDocument.Parse(OutpostCodec.Write(answer));

        Assert.Equal("25000", written.RootElement.GetProperty("pollTimeoutMs").GetRawText());
        Assert.Equal("10000", written.RootElement.GetProperty("leaseGraceMs").GetRawText());
    }

    [Fact]
    public void An_identity_and_a_binary_value_are_written_in_their_forms()
    {
        OutpostPairAnswer answer = new(
            Time,
            Sample.Agent,
            Sample.PublicKey,
            Sample.PublicKey,
            TimeSpan.FromSeconds(25),
            TimeSpan.FromSeconds(10));

        string text = Text(OutpostCodec.Write(answer));

        Assert.Contains($"\"agent\":\"{Sample.AgentText}\"", text);
        Assert.Contains($"\"serverKey\":\"{Sample.PublicKeyText}\"", text);
        Assert.Contains($"\"key\":\"{Sample.PublicKeyText}\"", text);
    }

    [Fact]
    public void The_parameters_of_a_command_are_written_again_by_the_rules_of_the_writers()
    {
        // The parameters as a caller's JSON may hold them: white space, and escapes that the writers do not write.
        using JsonDocument parameters = JsonDocument.Parse(
            """{ "n\u0061me" : "\u00e9\/\n" , "rows" : [ 1 , 2 ] }""");

        OutpostCommandMessage command = new(
            OutpostProtocol.Version,
            Sample.Command,
            Sample.Agent,
            "accounting",
            OutpostModeEnum.Read,
            null,
            "Result = 1;",
            parameters.RootElement,
            Time,
            Time.AddSeconds(30),
            TimeSpan.FromMinutes(2),
            16 * 1024 * 1024);

        string text = Text(OutpostCodec.Write(command));

        Assert.Contains("\"parameters\":{\"name\":\"\u00e9/\\n\",\"rows\":[1,2]}", text);
        Assert.Contains("\"callerKey\":null", text);
        Assert.Contains("\"mode\":\"read\"", text);
    }

    [Fact]
    public void The_outcome_object_of_a_request_is_written_as_the_bytes_it_was_given()
    {
        // The executor is not a writer of the library: its object keeps its own form, white space and escapes
        // included (§8).
        const string Outcome =
            """{ "kind" : "succeeded", "code" : null, "result" : "\u00e9\/", "errorText" : null, "messages" : [ ], "messagesTrimmed" : false }""";

        byte[] body = OutpostCodec.Write(new OutpostOutcomeRequest(Sample.Command, MessageText.Utf8(Outcome)));

        string text = Text(body);
        Assert.Contains("\"outcome\":" + Outcome, text);
        Assert.Contains($"\"command\":\"{Sample.CommandText}\"", text);
    }

    private static OutpostPollRequest Poll()
    {
        OutpostPollMode mode = new(1, TimeSpan.FromMinutes(10));

        return new OutpostPollRequest(
            639307665001230000,
            Time,
            "0.1.0",
            TimeSpan.FromSeconds(25),
            [new OutpostPollBase("accounting", mode, mode), new OutpostPollBase("warehouse", mode, null)],
            [Sample.Command]);
    }

    private static string Text(byte[] body)
    {
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(body);
    }
}
