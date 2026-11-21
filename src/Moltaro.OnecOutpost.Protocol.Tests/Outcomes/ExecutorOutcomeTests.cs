using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Protocol.Tests.Outcomes;

// The paragraph "The executor" of §8 of docs/protocol.md: the executor may hand the agent only `succeeded`, `failed`
// with `script-error`, `result-type` or `result-too-large`, or, for a Write, `unknown` with `transaction-ended`. The
// agent turns anything else into `result-type`.
public sealed class ExecutorOutcomeTests
{
    public static TheoryData<string, string, string?> Combinations => OutcomeTable.Combinations;

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(Combinations))]
    public void The_executor_may_hand_over_exactly_the_outcomes_that_the_protocol_gives_it(string mode, string kind, string? code)
    {
        bool allowed = OutpostOutcomeCodes.IsExecutorOutcome(OutcomeTable.Mode(mode), OutcomeTable.Kind(kind), code);

        Assert.Equal(OutcomeTable.ExecutorMayHandOver(mode, kind, code), allowed);
    }
}
