using System.Text.Json;

namespace Moltaro.OnecOutpost.Server.Tests.Outcomes;

// The factories of OutpostOutcome, with which an application fakes IOutpostServer in its own tests: each kind holds
// what its factory takes, and nothing else.
public sealed class OutcomeFactoryTests
{
    private static readonly Guid CommandId = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

    [Fact]
    public void The_default_kind_claims_nothing()
    {
        Assert.Equal(OutpostOutcomeKindEnum.Unknown, default);
    }

    [Fact]
    public void A_success_holds_its_command_its_result_and_its_messages()
    {
        using JsonDocument result = JsonDocument.Parse("""{ "total": 42 }""");

        OutpostOutcome outcome = OutpostOutcome.Succeeded(CommandId, result.RootElement, ["Posted."], messagesTrimmed: true);

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(CommandId, outcome.CommandId);
        Assert.Equal(42, outcome.Result!.Value.GetProperty("total").GetInt32());
        Assert.Null(outcome.Code);
        Assert.Null(outcome.ErrorText);
        Assert.Equal(["Posted."], outcome.Messages);
        Assert.True(outcome.MessagesTrimmed);
    }

    [Fact]
    public void A_success_whose_script_set_no_result_holds_a_JSON_null()
    {
        using JsonDocument result = JsonDocument.Parse("null");

        OutpostOutcome outcome = OutpostOutcome.Succeeded(CommandId, result.RootElement, [], messagesTrimmed: false);

        Assert.Equal(JsonValueKind.Null, outcome.Result!.Value.ValueKind);
    }

    // The default element belongs to no document: it is a missing value, not a result.
    [Fact]
    public void A_success_without_a_result_is_refused()
    {
        Assert.Throws<ArgumentException>(() => OutpostOutcome.Succeeded(CommandId, default, [], messagesTrimmed: false));
    }

    [Fact]
    public void A_success_or_a_failure_with_null_messages_is_refused()
    {
        using JsonDocument result = JsonDocument.Parse("1");

        Assert.Throws<ArgumentNullException>(
            () => OutpostOutcome.Succeeded(CommandId, result.RootElement, null!, messagesTrimmed: false));
        Assert.Throws<ArgumentNullException>(
            () => OutpostOutcome.Failed(CommandId, OutpostCodes.Lost, null, null!, messagesTrimmed: false));
    }

    [Fact]
    public void The_result_stays_valid_after_the_document_it_came_from_is_disposed()
    {
        JsonDocument result = JsonDocument.Parse("""[ "a", "b" ]""");
        OutpostOutcome outcome = OutpostOutcome.Succeeded(CommandId, result.RootElement, [], messagesTrimmed: false);

        result.Dispose();

        Assert.Equal("b", outcome.Result!.Value[1].GetString());
    }

    [Fact]
    public void A_failure_holds_its_command_its_code_its_error_text_and_its_messages()
    {
        OutpostOutcome outcome = OutpostOutcome.Failed(
            CommandId,
            OutpostCodes.ScriptError,
            "Division by zero",
            ["Checked 3 lines."],
            messagesTrimmed: true);

        Assert.Equal(OutpostOutcomeKindEnum.Failed, outcome.Kind);
        Assert.Equal(CommandId, outcome.CommandId);
        Assert.Null(outcome.Result);
        Assert.Equal("script-error", outcome.Code);
        Assert.Equal("Division by zero", outcome.ErrorText);
        Assert.Equal(["Checked 3 lines."], outcome.Messages);
        Assert.True(outcome.MessagesTrimmed);
    }

    [Fact]
    public void The_messages_are_copied_so_that_a_later_change_of_the_list_has_no_effect()
    {
        using JsonDocument result = JsonDocument.Parse("1");
        List<string> messages = ["first"];

        OutpostOutcome success = OutpostOutcome.Succeeded(CommandId, result.RootElement, messages, messagesTrimmed: false);
        OutpostOutcome failure = OutpostOutcome.Failed(CommandId, OutpostCodes.Lost, null, messages, messagesTrimmed: false);
        messages.Add("second");

        Assert.Equal(["first"], success.Messages);
        Assert.Equal(["first"], failure.Messages);
    }

    [Fact]
    public void A_refusal_holds_its_code_and_its_command_when_one_was_created()
    {
        OutpostOutcome withCommand = OutpostOutcome.Refused(CommandId, OutpostCodes.NotServed);
        OutpostOutcome withoutCommand = OutpostOutcome.Refused(null, OutpostCodes.UnknownBase);

        Assert.Equal(OutpostOutcomeKindEnum.Refused, withCommand.Kind);
        Assert.Equal(CommandId, withCommand.CommandId);
        Assert.Equal("not-served", withCommand.Code);
        AssertHoldsNothingElse(withCommand);

        Assert.Null(withoutCommand.CommandId);
        Assert.Equal("unknown-base", withoutCommand.Code);
    }

    [Fact]
    public void Unavailable_has_no_command_and_no_code()
    {
        OutpostOutcome outcome = OutpostOutcome.Unavailable();

        Assert.Equal(OutpostOutcomeKindEnum.Unavailable, outcome.Kind);
        Assert.Null(outcome.CommandId);
        Assert.Null(outcome.Code);
        AssertHoldsNothingElse(outcome);
    }

    [Fact]
    public void An_expiry_holds_its_command_and_no_code()
    {
        OutpostOutcome outcome = OutpostOutcome.Expired(CommandId);

        Assert.Equal(OutpostOutcomeKindEnum.Expired, outcome.Kind);
        Assert.Equal(CommandId, outcome.CommandId);
        Assert.Null(outcome.Code);
        AssertHoldsNothingElse(outcome);
    }

    [Fact]
    public void An_unknown_outcome_holds_its_command_and_its_code()
    {
        OutpostOutcome outcome = OutpostOutcome.Unknown(CommandId, OutpostCodes.AgentStopped);

        Assert.Equal(OutpostOutcomeKindEnum.Unknown, outcome.Kind);
        Assert.Equal(CommandId, outcome.CommandId);
        Assert.Equal("agent-stopped", outcome.Code);
        AssertHoldsNothingElse(outcome);
    }

    // What only a success or a failure holds: a result, an error text and messages.
    private static void AssertHoldsNothingElse(OutpostOutcome outcome)
    {
        Assert.Null(outcome.Result);
        Assert.Null(outcome.ErrorText);
        Assert.Empty(outcome.Messages);
        Assert.False(outcome.MessagesTrimmed);
    }
}
