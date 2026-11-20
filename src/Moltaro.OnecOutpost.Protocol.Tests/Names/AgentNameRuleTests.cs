namespace Moltaro.OnecOutpost.Protocol.Tests.Names;

// The rule of agent names, stated in full. The server checks it from this one place, so its tests show only that it
// applies it.
public sealed class AgentNameRuleTests
{
    public static TheoryData<string> GoodNames =>
    [
        "a",
        new string('n', 100),
        "Agent of the warehouse, room 2",
        "Entrep\u00f4t",
    ];

    public static TheoryData<string> BrokenNames =>
    [
        "",
        new string('n', 101),
        " agent",
        "agent ",
        "first line\nsecond line",
    ];

    [Theory]
    [MemberData(nameof(GoodNames))]
    public void A_name_of_1_to_100_characters_without_control_characters_or_white_space_at_its_ends_is_an_agent_name(string name)
    {
        OutpostNames.CheckAgentName(name, "agentName");
    }

    [Theory]
    [MemberData(nameof(BrokenNames))]
    public void Any_other_name_is_refused_with_the_name_of_its_parameter(string name)
    {
        Assert.Throws<ArgumentException>("agentName", () => OutpostNames.CheckAgentName(name, "agentName"));
    }

    [Fact]
    public void A_null_agent_name_is_refused_with_the_name_of_its_parameter()
    {
        Assert.Throws<ArgumentNullException>("agentName", () => OutpostNames.CheckAgentName(null!, "agentName"));
    }
}
