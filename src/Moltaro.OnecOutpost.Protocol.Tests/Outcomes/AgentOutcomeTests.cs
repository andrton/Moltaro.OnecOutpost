using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Protocol.Tests.Outcomes;

// The table "The outcomes an agent may send, by mode" of §8 of docs/protocol.md: every outcome of the table is the
// agent's, and every other combination of a kind and a code is the server's.
public sealed class AgentOutcomeTests
{
    public static TheoryData<string, string, string?> Combinations =>
        OutcomeTable.CombinationsWithoutTheAgentOutcomesOfTheConnectionTo1C;

    public static TheoryData<string, string, string?> AgentOutcomesOfTheConnectionTo1C =>
        OutcomeTable.AgentOutcomesOfTheConnectionTo1C;

    [Theory]
    [MemberData(nameof(Combinations))]
    public void An_agent_may_send_exactly_the_outcomes_that_the_table_gives_its_mode(string mode, string kind, string? code)
    {
        AssertAllowedExactlyAsTheTableSays(mode, kind, code);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(AgentOutcomesOfTheConnectionTo1C))]
    public void An_agent_may_send_the_outcomes_of_the_connection_to_1C_that_the_table_gives_its_mode(
        string mode,
        string kind,
        string? code)
    {
        AssertAllowedExactlyAsTheTableSays(mode, kind, code);
    }

    private static void AssertAllowedExactlyAsTheTableSays(string mode, string kind, string? code)
    {
        bool allowed = OutpostOutcomeCodes.IsAgentOutcome(OutcomeTable.Mode(mode), OutcomeTable.Kind(kind), code);

        Assert.Equal(OutcomeTable.AgentMaySend(mode, kind, code), allowed);
    }
}
