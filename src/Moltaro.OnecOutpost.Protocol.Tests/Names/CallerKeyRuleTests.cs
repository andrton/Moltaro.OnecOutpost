namespace Moltaro.OnecOutpost.Protocol.Tests.Names;

// The rule of caller keys, stated in full. The server checks it from this one place, so its tests show only that it
// applies it.
public sealed class CallerKeyRuleTests
{
    public static TheoryData<string> GoodKeys =>
    [
        "!",
        "~",
        new string('k', 200),
        "invoice:2026/42#A",
    ];

    public static TheoryData<string> BrokenKeys =>
    [
        "",
        new string('k', 201),
        "two keys",
        "key\u007F",
        "cl\u00e9",
    ];

    [Theory]
    [MemberData(nameof(GoodKeys))]
    public void A_key_of_1_to_200_characters_from_U0021_to_U007E_is_a_caller_key(string key)
    {
        OutpostNames.CheckCallerKey(key, "callerKey");
    }

    [Theory]
    [MemberData(nameof(BrokenKeys))]
    public void Any_other_key_is_refused_with_the_name_of_its_parameter(string key)
    {
        Assert.Throws<ArgumentException>("callerKey", () => OutpostNames.CheckCallerKey(key, "callerKey"));
    }

    [Fact]
    public void A_null_caller_key_is_refused_with_the_name_of_its_parameter()
    {
        Assert.Throws<ArgumentNullException>("callerKey", () => OutpostNames.CheckCallerKey(null!, "callerKey"));
    }
}
