using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Protocol.Tests.Outcomes;

// What the pool accepts from a COM worker (§8 of docs/protocol.md): what the executor hands over, succeeded, failed
// with script-error, result-type or result-too-large, or, for a Write, unknown with transaction-ended; and the
// outcomes the COM worker writes itself, Failed base-unreachable in
// either mode, Failed connection-lost for a Read, and Unknown connection-lost and Unknown result-type for a Write. The
// agent turns anything else into result-type.
public sealed class ComWorkerOutcomeTests
{
    public static TheoryData<string, string, string?> Combinations => OutcomeTable.Combinations;

    [Theory]
    [MemberData(nameof(Combinations))]
    public void A_COM_worker_may_hand_over_exactly_the_outcomes_of_the_executor_and_those_it_writes_itself(
        string mode,
        string kind,
        string? code)
    {
        bool allowed = OutpostOutcomeCodes.IsComWorkerOutcome(OutcomeTable.Mode(mode), OutcomeTable.Kind(kind), code);

        Assert.Equal(OutcomeTable.ComWorkerMayHandOver(mode, kind, code), allowed);
    }
}
