namespace Moltaro.OnecOutpost.Protocol.Tests.Names;

// The rule of scripts, stated in full. The server checks it from this one place, so its tests show only that it
// applies it.
public sealed class ScriptRuleTests
{
    public static TheoryData<string> GoodScripts =>
    [
        "a",
        "Result = 1;",
        "\n  Query = New Query(\"SELECT 1\");\n  Result = Query.Execute().IsEmpty();\n",

        // The emoji U+1F600, a surrogate pair, which UTF-8 carries.
        "Result = \"\U0001F600\";",
    ];

    public static TheoryData<string> BrokenScripts =>
    [
        "",
        " \r\n\t ",
        "Result = \"\0\";",

        // A half of the emoji U+1F600 next to a character that is not its other half: its high surrogate, then its low
        // one. The rows differ beyond their surrogates, since dotnet test tells rows apart as if every lone surrogate
        // were U+FFFD, and reports two rows that differ only there as one.
        "Result = \"\uD83D!\";",
        "Result = \"!\uDE00\";",
    ];

    [Theory]
    [MemberData(nameof(GoodScripts))]
    public void A_text_with_a_character_that_is_not_white_space_and_without_U0000_or_a_lone_surrogate_is_a_script(string script)
    {
        OutpostNames.CheckScript(script, "script");
    }

    [Theory]
    [MemberData(nameof(BrokenScripts))]
    public void Any_other_text_is_refused_with_the_name_of_its_parameter(string script)
    {
        Assert.Throws<ArgumentException>("script", () => OutpostNames.CheckScript(script, "script"));
    }

    [Fact]
    public void A_null_script_is_refused_with_the_name_of_its_parameter()
    {
        Assert.Throws<ArgumentNullException>("script", () => OutpostNames.CheckScript(null!, "script"));
    }
}
