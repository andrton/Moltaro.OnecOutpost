using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Protocol.Tests.Outcomes;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The outcome object (§8 of docs/protocol.md) and the rules of its members for the mode of its command: which
// outcomes an agent may send, and what each kind carries. An object that breaks §2 or a rule of §8 is refused with
// `form`. The object is not signed on its own: it travels inside a verified request, or lies in the outbox of the
// agent, and the agent checks the object of the executor with the same reader.
public sealed class OutcomeObjectTests
{
    // Every combination of a mode, a kind and a code, and the same combinations for a command that the server does
    // not know, whose mode is null.
    public static TheoryData<string?, string, string?> Combinations
    {
        get
        {
            TheoryData<string?, string, string?> combinations = [];

            foreach (string? mode in new[] { "read", "write", null })
            {
                foreach (string kind in OutcomeTable.Kinds)
                {
                    foreach (string? code in OutcomeTable.Codes)
                    {
                        combinations.Add(mode, kind, code);
                    }
                }
            }

            return combinations;
        }
    }

    // A mode, and an outcome of a kind and a code that the mode allows, with a member that the kind does not carry.
    public static TheoryData<string, string> OutcomesThatBreakTheRulesOfTheirMembers => new()
    {
        // `result` is null for every kind but `succeeded`.
        { "read", Outcome("failed", "run-limit", result: "1") },
        { "read", Outcome("failed", "script-error", result: "{}", errorText: "\"text\"") },
        { "read", Outcome("refused", "not-served", result: "\"text\"") },
        { "write", Outcome("unknown", "run-limit", result: "[]") },

        // `errorText` is null for everything but `failed` with `script-error`.
        { "read", Outcome("succeeded", null, errorText: "\"text\"") },
        { "read", Outcome("failed", "result-type", errorText: "\"text\"") },
        { "read", Outcome("refused", "expired", errorText: "\"text\"") },
        { "write", Outcome("unknown", "transaction-ended", errorText: "\"text\"") },

        // `messages` is empty, and `messagesTrimmed` false, for `refused` and `unknown`.
        { "read", Outcome("refused", "not-served", messages: "[\"text\"]") },
        { "write", Outcome("unknown", "agent-stopped", messages: "[\"text\"]") },
        { "read", Outcome("refused", "not-served", messagesTrimmed: "true") },
        { "write", Outcome("unknown", "agent-stopped", messagesTrimmed: "true") },

        // `messages` is an array of strings.
        { "read", Outcome("succeeded", null, messages: "[\"text\",1]") },
        { "read", Outcome("succeeded", null, messages: "[null]") },
        { "read", Outcome("succeeded", null, messages: "\"text\"") },
        { "read", Outcome("succeeded", null, messages: "null") },

        // Only the server sets Expired and Unavailable, and a kind is written in lowercase.
        { "read", Outcome("expired", null) },
        { "read", Outcome("unavailable", null) },
        { "read", Outcome("Succeeded", null) },
        { "read", Outcome("succeeded", null).Replace("\"kind\":\"succeeded\"", "\"kind\":null") },

        // Values of the wrong type.
        { "read", Outcome("failed", "run-limit").Replace("\"code\":\"run-limit\"", "\"code\":7") },
        { "read", Outcome("succeeded", null, errorText: "7") },
        { "read", Outcome("succeeded", null, messagesTrimmed: "\"false\"") },
        { "read", Outcome("succeeded", null, messagesTrimmed: "null") },

        // Not exactly the members of the object, each once.
        { "read", Outcome("succeeded", null).Replace("\"code\":null,", string.Empty) },
        { "read", Outcome("succeeded", null).Replace("\"result\":null,", string.Empty) },
        { "read", Outcome("succeeded", null).Replace("\"errorText\":null,", string.Empty) },
        { "read", Outcome("succeeded", null).Replace("\"messages\":[],", string.Empty) },
        { "read", Outcome("succeeded", null).Replace("\"messages\":[],", "\"messages\":[],\"command\":null,") },
        { "read", Outcome("succeeded", null).Replace("\"messages\":[],", "\"messages\":[],\"messages\":[],") },

        // A result deeper than 64 levels.
        { "read", Outcome("succeeded", null, result: MessageText.Nested(65)) },
        { "write", Outcome("succeeded", null, result: "[" + MessageText.Nested(64) + "]") },
    };

    // An outcome that an agent may send for a Read, with U+0000 or a lone surrogate somewhere in it, written as an
    // escape: no string of the protocol holds either.
    public static TheoryData<string> OutcomesWithACharacterThatNoStringHolds =>
    [
        Outcome("succeeded", null, result: "\"\\u0000\""),
        Outcome("succeeded", null, result: """{"a":[1,{"b":"text\u0000"}]}"""),
        Outcome("succeeded", null, result: """{"a":[1,{"\u0000":2}]}"""),
        Outcome("succeeded", null, messages: """["one","\u0000"]"""),
        Outcome("failed", "script-error", errorText: "\"an error\\u0000\""),

        // A high surrogate without its low one, a low one without its high one, the two in the wrong order, and a
        // high one before a character that is no surrogate.
        Outcome("succeeded", null, result: "\"\\ud83d\""),
        Outcome("succeeded", null, result: """{"a":[1,{"b":"\ude00"}]}"""),
        Outcome("succeeded", null, result: """{"a":[1,{"\ude00\ud83d":2}]}"""),
        Outcome("succeeded", null, messages: """["one","\ud83d!"]"""),
        Outcome("failed", "script-error", errorText: "\"\\ud83d\\ud83d\\ude00\""),
    ];

    [Theory]
    [MemberData(nameof(Combinations))]
    public void An_outcome_is_read_only_with_a_kind_and_a_code_that_an_agent_may_send_for_the_mode_and_for_either_mode_when_the_mode_is_not_known(
        string? mode,
        string kind,
        string? code)
    {
        bool allowed = mode is null
            ? OutcomeTable.AgentMaySend("read", kind, code) || OutcomeTable.AgentMaySend("write", kind, code)
            : OutcomeTable.AgentMaySend(mode, kind, code);

        // The other members are those that the kind carries, so that only the kind and the code are at fault.
        string outcome = Outcome(
            kind,
            code,
            result: kind == "succeeded" ? "{\"rows\":3}" : "null",
            errorText: kind == "failed" && code == "script-error" ? "\"An error of 1C\"" : "null");

        if (allowed)
        {
            OutpostOutcomeMessage read = Read(mode, outcome);

            Assert.Equal(OutcomeTable.Kind(kind), read.Kind);
            Assert.Equal(code, read.Code);
        }
        else
        {
            ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(mode, outcome));
        }
    }

    [Fact]
    public void An_outcome_that_succeeded_is_read_with_its_result_and_its_messages()
    {
        OutpostOutcomeMessage outcome = Read(
            "read",
            Outcome("succeeded", null, result: "{\"rows\":3}", messages: "[\"one\",\"two\"]", messagesTrimmed: "true"));

        Assert.Equal(OutpostAgentOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Null(outcome.Code);
        Assert.NotNull(outcome.Result);
        Assert.Equal(3, outcome.Result.Value.GetProperty("rows").GetInt32());
        Assert.Null(outcome.ErrorText);
        Assert.Equal<string>(["one", "two"], outcome.Messages);
        Assert.True(outcome.MessagesTrimmed);
    }

    [Theory]
    [InlineData("null", JsonValueKind.Null)]
    [InlineData("true", JsonValueKind.True)]
    [InlineData("12.5", JsonValueKind.Number)]
    [InlineData("\"text\"", JsonValueKind.String)]
    [InlineData("[1,[2]]", JsonValueKind.Array)]
    public void The_result_of_an_outcome_that_succeeded_is_any_value_and_a_null_result_is_a_value_too(
        string result,
        JsonValueKind kind)
    {
        OutpostOutcomeMessage outcome = Read("write", Outcome("succeeded", null, result: result));

        Assert.NotNull(outcome.Result);
        Assert.Equal(kind, outcome.Result.Value.ValueKind);
    }

    [Fact]
    public void A_result_of_exactly_64_levels_is_read()
    {
        OutpostOutcomeMessage outcome = Read("read", Outcome("succeeded", null, result: MessageText.Nested(64)));

        Assert.NotNull(outcome.Result);
        Assert.Equal(64, OutpostCodec.ValueDepth(outcome.Result.Value));
    }

    [Fact]
    public void An_outcome_that_failed_in_1C_is_read_with_its_error_text_and_no_result()
    {
        OutpostOutcomeMessage outcome = Read(
            "write",
            Outcome(
                "failed",
                "script-error",
                errorText: "\"Failed to call the context method (Write)\"",
                messages: "[\"The field \\\"Counterparty\\\" is not filled in\"]",
                messagesTrimmed: "true"));

        Assert.Equal(OutpostAgentOutcomeKindEnum.Failed, outcome.Kind);
        Assert.Equal("script-error", outcome.Code);
        Assert.Null(outcome.Result);
        Assert.Equal("Failed to call the context method (Write)", outcome.ErrorText);
        Assert.Equal("The field \"Counterparty\" is not filled in", Assert.Single(outcome.Messages));
        Assert.True(outcome.MessagesTrimmed);
    }

    [Theory]
    [InlineData("refused", "not-addressed")]
    [InlineData("unknown", "transaction-ended")]
    public void An_outcome_that_was_refused_or_is_unknown_is_read_with_its_code_and_nothing_else(string kind, string code)
    {
        OutpostOutcomeMessage outcome = Read("write", Outcome(kind, code));

        Assert.Equal(OutcomeTable.Kind(kind), outcome.Kind);
        Assert.Equal(code, outcome.Code);
        Assert.Null(outcome.Result);
        Assert.Null(outcome.ErrorText);
        Assert.Empty(outcome.Messages);
        Assert.False(outcome.MessagesTrimmed);
    }

    [Theory]
    [MemberData(nameof(OutcomesThatBreakTheRulesOfTheirMembers))]
    public void An_outcome_that_breaks_a_rule_of_its_members_is_refused_with_form(string mode, string outcome)
    {
        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read(mode, outcome));
    }

    [Theory]
    [MemberData(nameof(OutcomesWithACharacterThatNoStringHolds))]
    public void U0000_and_a_lone_surrogate_are_refused_with_form_anywhere_in_the_object(string outcome)
    {
        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => Read("read", outcome));
    }

    [Fact]
    public void A_surrogate_pair_is_read_as_its_character()
    {
        OutpostOutcomeMessage outcome = Read(
            "read",
            Outcome("failed", "script-error", errorText: "\"\\ud83d\\ude00\"", messages: "[\"\U0001F600\"]"));

        Assert.Equal("\U0001F600", outcome.ErrorText);
        Assert.Equal("\U0001F600", Assert.Single(outcome.Messages));
    }

    // CESU-8, the bytes that a writer gives when it encodes a lone UTF-16 surrogate by itself: no UTF-8.
    [Fact]
    public void A_lone_surrogate_written_as_bytes_is_refused_with_form()
    {
        byte[] before = MessageText.Utf8("{\"kind\":\"succeeded\",\"code\":null,\"result\":\"");
        byte[] after = MessageText.Utf8("\",\"errorText\":null,\"messages\":[],\"messagesTrimmed\":false}");
        byte[] outcome = [.. before, 0xED, 0xA0, 0xBD, .. after];

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => OutpostCodec.ReadOutcome(OutpostModeEnum.Read, outcome));
    }

    [Fact]
    public void An_outcome_of_every_kind_round_trips()
    {
        using JsonDocument result = JsonDocument.Parse("{\"rows\":[1,2.5,null,\"\u00e9\"]}");
        using JsonDocument nothing = JsonDocument.Parse("null");

        OutpostOutcomeMessage[] outcomes =
        [
            new(OutpostAgentOutcomeKindEnum.Succeeded, null, result.RootElement, null, ["one", "two \"quoted\"\r\n"], true),
            new(OutpostAgentOutcomeKindEnum.Succeeded, null, nothing.RootElement, null, [], false),
            new(OutpostAgentOutcomeKindEnum.Failed, "script-error", null, "An error of 1C: \u00e9 \U0001F600", ["one"], true),
            new(OutpostAgentOutcomeKindEnum.Failed, "result-too-large", null, null, [], false),
            new(OutpostAgentOutcomeKindEnum.Refused, "malformed", null, null, [], false),
            new(OutpostAgentOutcomeKindEnum.Unknown, "agent-stopped", null, null, [], false),
        ];

        foreach (OutpostOutcomeMessage written in outcomes)
        {
            OutpostOutcomeMessage read = OutpostCodec.ReadOutcome(OutpostModeEnum.Write, OutpostCodec.Write(written));

            Assert.Equal(written.Kind, read.Kind);
            Assert.Equal(written.Code, read.Code);
            Assert.Equal(written.Result.HasValue, read.Result.HasValue);
            Assert.True(!written.Result.HasValue || JsonElement.DeepEquals(written.Result.Value, read.Result!.Value));
            Assert.Equal(written.ErrorText, read.ErrorText);
            Assert.Equal<string>(written.Messages, read.Messages);
            Assert.Equal(written.MessagesTrimmed, read.MessagesTrimmed);
        }
    }

    // The outcome object with its members in the order of the protocol, each as raw JSON but the kind and the code.
    private static string Outcome(
        string kind,
        string? code,
        string result = "null",
        string errorText = "null",
        string messages = "[]",
        string messagesTrimmed = "false")
    {
        return new MessageText(
            ("kind", MessageText.Quote(kind)),
            ("code", code is null ? "null" : MessageText.Quote(code)),
            ("result", result),
            ("errorText", errorText),
            ("messages", messages),
            ("messagesTrimmed", messagesTrimmed)).ToString();
    }

    private static OutpostOutcomeMessage Read(string? mode, string outcome)
    {
        OutpostModeEnum? modeOfTheCommand = mode is null ? null : OutcomeTable.Mode(mode);

        return OutpostCodec.ReadOutcome(modeOfTheCommand, MessageText.Utf8(outcome));
    }
}
