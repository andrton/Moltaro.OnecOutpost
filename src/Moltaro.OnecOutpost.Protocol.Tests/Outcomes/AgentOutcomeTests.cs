using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Protocol.Tests.Outcomes;

// The table "The outcomes an agent may send, by mode" of §8 of docs/protocol.md: every outcome of the table is the
// agent's, and every other combination of a kind and a code is the server's.
public sealed class AgentOutcomeTests
{
    public static TheoryData<string, string, string?> Combinations => OutcomeTable.Combinations;

    [Theory]
    [MemberData(nameof(Combinations))]
    public void An_agent_may_send_exactly_the_outcomes_that_the_table_gives_its_mode(string mode, string kind, string? code)
    {
        bool allowed = OutpostOutcomeCodes.IsAgentOutcome(OutcomeTable.Mode(mode), OutcomeTable.Kind(kind), code);

        Assert.Equal(OutcomeTable.AgentMaySend(mode, kind, code), allowed);
    }
}
