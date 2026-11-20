namespace Moltaro.OnecOutpost.Server.Tests.Outcomes;

// The factories accept a code only when it is one of OutpostCodes and belongs to the kind of the outcome, by the table
// of codes of the contract. The codes are written out as the contract gives them, so a constant that drifts fails too.
public sealed class OutcomeCodeTests
{
    private static readonly Guid CommandId = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

    // The code, and whether it belongs to Failed, to Refused and to Unknown.
    public static TheoryData<string, bool, bool, bool> CodeTable => new()
    {
        { "script-error", true, false, false },
        { "result-type", true, false, false },
        { "result-too-large", true, false, true },
        { "run-limit", true, false, true },
        { "lost", true, false, false },
        { "caller-key-conflict", false, true, false },
        { "unknown-base", false, true, false },
        { "mode-closed", false, true, false },
        { "not-served", false, true, false },
        { "not-addressed", false, true, false },
        { "protocol-version", false, true, false },
        { "expired", false, true, false },
        { "clock-skew", false, true, false },
        { "revoked", false, true, true },
        { "disconnected", false, false, true },
        { "agent-stopped", false, false, true },
        { "transaction-ended", false, false, true },
    };

    [Theory]
    [MemberData(nameof(CodeTable))]
    public void A_code_is_accepted_by_the_factories_of_its_kinds_and_refused_by_the_others(
        string code,
        bool failed,
        bool refused,
        bool unknown)
    {
        AssertAcceptedOnlyIf(failed, code, () => OutpostOutcome.Failed(CommandId, code, null, [], messagesTrimmed: false));
        AssertAcceptedOnlyIf(refused, code, () => OutpostOutcome.Refused(CommandId, code));
        AssertAcceptedOnlyIf(unknown, code, () => OutpostOutcome.Unknown(CommandId, code));
    }

    [Fact]
    public void A_text_that_is_not_a_code_is_refused_by_every_factory()
    {
        Assert.Throws<ArgumentException>(() => OutpostOutcome.Failed(CommandId, "timeout", null, [], messagesTrimmed: false));
        Assert.Throws<ArgumentException>(() => OutpostOutcome.Refused(CommandId, "timeout"));
        Assert.Throws<ArgumentException>(() => OutpostOutcome.Unknown(CommandId, "timeout"));
    }

    [Fact]
    public void A_null_code_is_refused_by_every_factory()
    {
        Assert.Throws<ArgumentNullException>(() => OutpostOutcome.Failed(CommandId, null!, null, [], messagesTrimmed: false));
        Assert.Throws<ArgumentNullException>(() => OutpostOutcome.Refused(CommandId, null!));
        Assert.Throws<ArgumentNullException>(() => OutpostOutcome.Unknown(CommandId, null!));
    }

    private static void AssertAcceptedOnlyIf(bool accepted, string code, Func<OutpostOutcome> create)
    {
        if (accepted)
        {
            Assert.Equal(code, create().Code);
        }
        else
        {
            Assert.Throws<ArgumentException>(create);
        }
    }
}
