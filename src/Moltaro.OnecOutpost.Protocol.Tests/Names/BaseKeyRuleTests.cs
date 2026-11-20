namespace Moltaro.OnecOutpost.Protocol.Tests.Names;

// The rule of base keys, stated in full. The server and the agent check it from this one place, so their tests show
// only that they apply it.
public sealed class BaseKeyRuleTests
{
    public static TheoryData<string> GoodKeys =>
    [
        "a",
        "7",
        new string('k', 64),
        "accounting-2026_main.copy",
    ];

    public static TheoryData<string> BrokenKeys =>
    [
        "",
        new string('k', 65),
        "-accounting",
        "_accounting",
        ".accounting",
        "Accounting",
        "caf\u00e9",
        "two keys",
        "accounting/main",
    ];

    [Theory]
    [MemberData(nameof(GoodKeys))]
    public void A_key_of_1_to_64_characters_of_a_z_0_9_hyphen_underscore_and_dot_starting_with_a_letter_or_a_digit_is_a_base_key(string key)
    {
        Assert.True(OutpostNames.IsBaseKey(key));

        OutpostNames.CheckBaseKey(key, "baseKey");
    }

    [Theory]
    [MemberData(nameof(BrokenKeys))]
    public void Any_other_key_is_refused_with_the_name_of_its_parameter(string key)
    {
        Assert.False(OutpostNames.IsBaseKey(key));

        Assert.Throws<ArgumentException>("baseKey", () => OutpostNames.CheckBaseKey(key, "baseKey"));
    }

    [Fact]
    public void A_null_base_key_is_refused_with_the_name_of_its_parameter()
    {
        Assert.Throws<ArgumentNullException>("baseKey", () => OutpostNames.CheckBaseKey(null!, "baseKey"));
    }
}
