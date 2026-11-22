using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Protocol;

namespace Moltaro.OnecOutpost.Agent.Tests.Policy;

// The rules of policy.json. A file that breaks any of them is invalid as a whole, and the error says where and what
// (Q1): the path of the member at fault, what is wrong with it, and what is expected there. Each test changes one
// member of a valid policy: the owner of the member is given by its path, with "" for the root.
public sealed class PolicyRuleTests
{
    // A string that WithEscapedString replaces, as a name or as a value.
    private const string Placeholder = "placeholder";

    // The owner's example of an error that says where and what (Q1), word for word.
    [Fact]
    public void An_empty_server_is_refused_with_an_error_that_says_where_and_what()
    {
        const string Expected = """
            bases[0].server: the value is empty; expected the 1C cluster address such as "srv-1c:1541", or remove "server" and "database" and give "file"
            """;

        JsonObject policy = ValidPolicy();
        Owner(policy, "bases[0]")["server"] = "";

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        Assert.Equal(Expected, exception.Message);
    }

    [Theory]
    [InlineData("", "auditRetentionDays", "1")]
    [InlineData("", "auditRetentionDays", "3650")]
    [InlineData("", "retryDelayMax", "\"00:00:01\"")]
    [InlineData("", "retryDelayMax", "\"01:00:00\"")]
    [InlineData("bases[0].read", "workers", "1")]
    [InlineData("bases[0].read", "workers", "8")]
    [InlineData("bases[0].read", "runLimit", "\"00:00:01\"")]
    [InlineData("bases[0].read", "runLimit", "\"1.00:00:00\"")]
    public void A_value_at_either_end_of_its_range_is_accepted(string owner, string member, string value)
    {
        JsonObject policy = ValidPolicy();
        Owner(policy, owner)[member] = JsonNode.Parse(value);

        AgentPolicyReader.Read(policy.ToJsonString());
    }

    [Theory]
    [InlineData("", "auditRetentionDays", "0")]
    [InlineData("", "auditRetentionDays", "3651")]
    [InlineData("", "auditRetentionDays", "\"90\"")]
    [InlineData("", "proxy", "\"https://proxy.local:3128\"")]
    [InlineData("", "proxy", "\"proxy.local\"")]
    [InlineData("", "proxy", "3128")]
    [InlineData("", "proxy", "null")]
    [InlineData("", "retryDelayMax", "\"00:00:00\"")]
    [InlineData("", "retryDelayMax", "\"01:00:01\"")]
    [InlineData("", "retryDelayMax", "\"30 seconds\"")]
    [InlineData("", "retryDelayMax", "30")]
    [InlineData("", "bases", "[]")]
    [InlineData("", "bases", "{}")]
    [InlineData("bases[0]", "key", "\"Accounting\"")]
    [InlineData("bases[0]", "key", "42")]
    [InlineData("bases[1]", "key", "\"accounting\"")]
    [InlineData("bases[0]", "server", "1541")]
    [InlineData("bases[0]", "read", "\"outpost-read\"")]
    [InlineData("bases[0]", "write", "true")]
    [InlineData("bases[0]", "write", "null")]
    [InlineData("bases[0].read", "account", "42")]
    [InlineData("bases[0].read", "workers", "0")]
    [InlineData("bases[0].read", "workers", "9")]
    [InlineData("bases[0].read", "workers", "\"1\"")]
    [InlineData("bases[0].read", "workers", "null")]
    [InlineData("bases[0].read", "runLimit", "\"00:00:00\"")]
    [InlineData("bases[0].read", "runLimit", "\"1.00:00:01\"")]
    [InlineData("bases[0].read", "runLimit", "\"10:00\"")]
    [InlineData("bases[0].read", "runLimit", "600")]
    [InlineData("bases[0].write", "workers", "9")]
    [InlineData("bases[1].read", "workers", "9")]
    public void A_value_that_breaks_the_rule_of_its_member_makes_the_policy_invalid_at_its_path(
        string owner,
        string member,
        string value)
    {
        JsonObject policy = ValidPolicy();
        Owner(policy, owner)[member] = JsonNode.Parse(value);

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        AssertSaysWhereAndWhat(exception, PathOf(owner, member));
    }

    // The four members that name something share one rule: an empty or white-space string names nothing.
    [Theory]
    [InlineData("bases[0]", "server", "")]
    [InlineData("bases[0]", "server", " \t ")]
    [InlineData("bases[0]", "database", "")]
    [InlineData("bases[0]", "database", " \t ")]
    [InlineData("bases[1]", "file", "")]
    [InlineData("bases[1]", "file", " \t ")]
    [InlineData("bases[0].read", "account", "")]
    [InlineData("bases[0].read", "account", " \t ")]
    public void An_empty_or_white_space_server_database_file_or_account_makes_the_policy_invalid_at_its_path(
        string owner,
        string member,
        string text)
    {
        JsonObject policy = ValidPolicy();
        Owner(policy, owner)[member] = text;

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        AssertSaysWhereAndWhat(exception, PathOf(owner, member));
    }

    // One object stands for all: every object of the file is read by the same rule.
    [Fact]
    public void A_member_given_twice_makes_the_policy_invalid_at_its_path()
    {
        const string Json = """
            { "bases": [ { "key": "accounting", "key": "warehouse", "file": "C:\\1C\\A", "read": { "account": "outpost-read" } } ] }
            """;

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(() => AgentPolicyReader.Read(Json));

        AssertSaysWhereAndWhat(exception, "bases[0].key");
    }

    [Theory]
    [InlineData("", "bases")]
    [InlineData("bases[0]", "key")]
    [InlineData("bases[0]", "read")]
    [InlineData("bases[0].read", "account")]
    public void A_required_member_that_is_left_out_makes_the_policy_invalid_at_its_path(string owner, string member)
    {
        JsonObject policy = ValidPolicy();
        Owner(policy, owner).Remove(member);

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        AssertSaysWhereAndWhat(exception, PathOf(owner, member));
    }

    [Theory]
    [InlineData("", "auditRetention")]
    [InlineData("bases[0]", "port")]
    [InlineData("bases[0].read", "password")]
    public void An_unknown_member_makes_the_policy_invalid_at_its_path(string owner, string member)
    {
        JsonObject policy = ValidPolicy();
        Owner(policy, owner)[member] = "anything";

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        AssertSaysWhereAndWhat(exception, PathOf(owner, member));
    }

    // Carried over from S2: requestTimeout is in the form of runLimit, from 10 seconds to 1 hour, and safeMode of write
    // is true or false.
    [Theory]
    [InlineData("", "requestTimeout", "\"00:00:10\"")]
    [InlineData("", "requestTimeout", "\"01:00:00\"")]
    [InlineData("bases[0].write", "safeMode", "true")]
    [InlineData("bases[0].write", "safeMode", "false")]
    public void A_request_timeout_at_either_end_of_its_range_and_a_safe_mode_of_write_are_accepted(
        string owner,
        string member,
        string value)
    {
        JsonObject policy = ValidPolicy();
        Owner(policy, owner)[member] = JsonNode.Parse(value);

        AgentPolicyReader.Read(policy.ToJsonString());
    }

    [Theory]
    [InlineData("", "requestTimeout", "\"00:00:09\"")]
    [InlineData("", "requestTimeout", "\"01:00:01\"")]
    [InlineData("", "requestTimeout", "\"5 minutes\"")]
    [InlineData("", "requestTimeout", "300")]
    [InlineData("", "requestTimeout", "null")]
    [InlineData("bases[0].write", "safeMode", "\"false\"")]
    [InlineData("bases[0].write", "safeMode", "0")]
    [InlineData("bases[0].write", "safeMode", "null")]
    public void A_request_timeout_or_a_safe_mode_of_write_that_breaks_its_rule_makes_the_policy_invalid_at_its_path(
        string owner,
        string member,
        string value)
    {
        JsonObject policy = ValidPolicy();
        Owner(policy, owner)[member] = JsonNode.Parse(value);

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        AssertSaysWhereAndWhat(exception, PathOf(owner, member));
    }

    // A Read always runs in safe mode, so safeMode stays an unknown member of read, even with a value that write takes.
    [Fact]
    public void A_safe_mode_in_read_makes_the_policy_invalid_at_its_path()
    {
        JsonObject policy = ValidPolicy();
        Owner(policy, "bases[0].read")["safeMode"] = true;

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        AssertSaysWhereAndWhat(exception, "bases[0].read.safeMode");
    }

    // At most 256 bases, as many as a poll declares (§6.2 and §10 of docs/protocol.md).
    [Fact]
    public void A_policy_of_256_bases_is_valid()
    {
        AgentPolicy policy = AgentPolicyReader.Read(PolicyOfBases(OutpostProtocol.MaxBases));

        Assert.Equal(OutpostProtocol.MaxBases, policy.Bases.Count);
    }

    [Fact]
    public void A_policy_of_257_bases_makes_the_policy_invalid_at_bases()
    {
        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(PolicyOfBases(OutpostProtocol.MaxBases + 1)));

        AssertSaysWhereAndWhat(exception, "bases");
    }

    // Q7: the agent offers the proxy the computer's credentials and never any written in the file. A user or a
    // password in the URL is refused, and neither is repeated, since either may be a secret (principle 9).
    [Theory]
    [InlineData("http://svc@proxy.example.com:3128")]
    [InlineData("http://svc:S3cret@proxy.example.com:3128")]
    [InlineData("http://:S3cret@proxy.example.com:3128")]
    public void A_proxy_URL_with_a_user_or_a_password_makes_the_policy_invalid_without_repeating_either(string proxy)
    {
        JsonObject policy = ValidPolicy();
        policy["proxy"] = proxy;

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        AssertSaysWhereAndWhat(exception, "proxy");
        Assert.DoesNotContain("svc", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("S3cret", exception.Message, StringComparison.Ordinal);
    }

    // The example host of the proxy is not a .local name.
    [Fact]
    public void The_error_of_a_proxy_that_is_not_an_http_URL_names_the_example_proxy_example_com()
    {
        JsonObject policy = ValidPolicy();
        policy["proxy"] = "https://gateway.example.org:3128";

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        AssertSaysWhereAndWhat(exception, "proxy");
        Assert.Contains("\"http://proxy.example.com:3128\"", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(".local", exception.Message, StringComparison.Ordinal);
    }

    // The contract names no single member at fault when the connection of a base is incomplete or doubled, so the
    // path is that of the base or of one of its connection members.
    [Theory]
    [InlineData("""{ "server": "srv-1c" }""")]
    [InlineData("""{ "database": "accounting" }""")]
    [InlineData("""{ }""")]
    [InlineData("""{ "server": "srv-1c", "database": "accounting", "file": "C:\\1C\\Accounting" }""")]
    [InlineData("""{ "server": "srv-1c", "file": "C:\\1C\\Accounting" }""")]
    public void A_base_without_either_a_server_and_a_database_or_a_file_makes_the_policy_invalid_at_that_base(
        string connection)
    {
        JsonObject policy = ValidPolicy();
        JsonObject cluster = Owner(policy, "bases[0]");
        cluster.Remove("server");
        cluster.Remove("database");

        foreach ((string name, JsonNode? value) in JsonNode.Parse(connection)!.AsObject())
        {
            cluster[name] = value?.DeepClone();
        }

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        Assert.Contains(exception.Path, new[] { "bases[0]", "bases[0].server", "bases[0].database", "bases[0].file" });
        AssertSaysWhereAndWhat(exception, exception.Path);
    }

    // A string of the file may hold a secret, such as a password in the URL of the proxy, and the error goes into the
    // audit log, so no refusal repeats a string (principle 9). The cases are a few refusals of a string: a proxy that is
    // not an http URL, a key that is not a base key, a time span of the wrong form, and a key that an earlier base has.
    // In each, the secret is a part of the refused string that the error must not hold.
    [Theory]
    [InlineData("", "proxy", "https://svc:S3cret@gateway.example:8080", "S3cret")]
    [InlineData("bases[0]", "key", "Key-S3cret", "S3cret")]
    [InlineData("", "retryDelayMax", "S3cret", "S3cret")]
    [InlineData("bases[1]", "key", "accounting", "accounting")]
    public void A_refused_string_is_not_repeated_in_the_error(string owner, string member, string value, string secret)
    {
        JsonObject policy = ValidPolicy();
        Owner(policy, owner)[member] = value;

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        AssertSaysWhereAndWhat(exception, PathOf(owner, member));
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    // A lone surrogate reaches the file only as an escape of JSON, since no UTF-8 can carry one: here a half of the
    // emoji U+1F600 without the other. The cases are a URL of the proxy that ends with the high half, an account that
    // starts with the low one, and a string where a number is expected. Each holds a secret that the error must not
    // repeat (principle 9).
    [Theory]
    [InlineData("", "proxy", "http://svc:S3cret@proxy.local:3128\\uD83D")]
    [InlineData("bases[0].read", "account", "\\uDE00S3cret")]
    [InlineData("bases[0].read", "workers", "S3cret\\uD83D")]
    public void An_escaped_lone_surrogate_in_a_string_makes_the_policy_invalid_at_its_path(
        string owner,
        string member,
        string escaped)
    {
        JsonObject policy = ValidPolicy();
        Owner(policy, owner)[member] = Placeholder;

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(WithEscapedString(policy, escaped)));

        AssertSaysWhereAndWhat(exception, PathOf(owner, member));
        Assert.DoesNotContain("S3cret", exception.Message, StringComparison.Ordinal);
    }

    // A name is not a value, so the path shows the name as the file writes it, escape included, as the path of an
    // unknown member shows its name.
    [Fact]
    public void An_escaped_lone_surrogate_in_a_member_name_makes_the_policy_invalid_at_its_path()
    {
        JsonObject policy = ValidPolicy();
        Owner(policy, "bases[0].read")[Placeholder] = "outpost-read";

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(WithEscapedString(policy, "acc\\uDC00ount")));

        AssertSaysWhereAndWhat(exception, "bases[0].read.acc\\uDC00ount");
    }

    // The two halves of the emoji U+1F600, escaped one after the other, are the emoji.
    [Fact]
    public void An_escaped_surrogate_pair_is_read_as_the_character_it_encodes()
    {
        JsonObject policy = ValidPolicy();
        Owner(policy, "bases[0].read")["account"] = Placeholder;

        AgentPolicy read = AgentPolicyReader.Read(WithEscapedString(policy, "outpost-\\uD83D\\uDE00"));

        Assert.Equal("outpost-\U0001F600", read.Bases[0].Read.Account);
    }

    // A policy written as a JSON string by mistake, for example encoded twice, holds the whole policy with its secrets.
    [Fact]
    public void A_file_that_is_a_string_is_refused_without_repeating_it()
    {
        const string Inner = """{ "proxy": "http://svc:S3cret@proxy.local:3128", "bases": [] }""";

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(JsonSerializer.Serialize(Inner)));

        Assert.Matches("; expected .+", exception.Message);
        Assert.DoesNotContain("S3cret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Inner, exception.Message, StringComparison.Ordinal);
    }

    // Each text would be a valid policy if comments were allowed or the text were complete.
    [Theory]
    [InlineData("""{ "bases": [ { "key": "a", "file": "C:\\1C\\A", "read": { "account": "r" } } ] } // the bases""")]
    [InlineData("""{ "bases": [ { "key": "a", "file": "C:\\1C\\A", "read": { "account": "r" } } ]""")]
    public void A_file_that_is_not_plain_JSON_makes_the_policy_invalid_and_says_what_is_expected(string json)
    {
        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(() => AgentPolicyReader.Read(json));

        Assert.Matches("; expected .+", exception.Message);
    }

    // The place is counted as a person counts it: lines and columns from 1. The line of the comment is ASCII, so its
    // column is plain to count: four spaces, then the comment.
    [Fact]
    public void A_file_that_is_not_plain_JSON_is_refused_with_the_line_and_the_column_of_the_fault()
    {
        const string Json = """
            {
              "bases": [
                // the bases of this machine
                { "key": "a", "file": "C:\\1C\\A", "read": { "account": "r" } }
              ]
            }
            """;

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(() => AgentPolicyReader.Read(Json));

        Assert.Matches(@"\bline 3\b", exception.Message);
        Assert.Matches(@"\bcolumn 5\b", exception.Message);
        Assert.Matches("; expected .+", exception.Message);
    }

    // The column counts characters, not the bytes of UTF-8. Before the comment on line 3 stand four spaces, a quote, five
    // Cyrillic letters of two bytes each in UTF-8, a quote and a space: the comment starts at column 13, at byte 18.
    [Fact]
    public void The_column_of_a_fault_counts_characters_not_bytes()
    {
        const string Json = "{\n  \"bases\": [\n    \"\u0421\u043a\u043b\u0430\u0434\" // the warehouse\n  ]\n}";

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(() => AgentPolicyReader.Read(Json));

        Assert.Matches(@"\bline 3\b", exception.Message);
        Assert.Matches(@"\bcolumn 13\b", exception.Message);
    }

    // Two bases, so that a path can name the second: one on a cluster with both modes, one in a folder that is only read.
    private static JsonObject ValidPolicy()
    {
        return new JsonObject
        {
            ["bases"] = new JsonArray
            {
                new JsonObject
                {
                    ["key"] = "accounting",
                    ["server"] = "srv-1c:1541",
                    ["database"] = "accounting",
                    ["read"] = new JsonObject { ["account"] = "outpost-read" },
                    ["write"] = new JsonObject { ["account"] = "outpost-write" },
                },
                new JsonObject
                {
                    ["key"] = "warehouse-file",
                    ["file"] = @"D:\1C\Warehouse",
                    ["read"] = new JsonObject { ["account"] = "outpost-read" },
                },
            },
        };
    }

    // A policy of so many bases in folders, each only read.
    private static string PolicyOfBases(int count)
    {
        JsonArray bases = [];

        for (int index = 0; index < count; index++)
        {
            bases.Add(new JsonObject
            {
                ["key"] = $"base-{index}",
                ["file"] = $@"D:\1C\Base{index}",
                ["read"] = new JsonObject { ["account"] = "outpost-read" },
            });
        }

        return new JsonObject { ["bases"] = bases }.ToJsonString();
    }

    private static JsonObject Owner(JsonObject policy, string owner)
    {
        return owner switch
        {
            "" => policy,
            "bases[0]" => policy["bases"]![0]!.AsObject(),
            "bases[0].read" => policy["bases"]![0]!["read"]!.AsObject(),
            "bases[0].write" => policy["bases"]![0]!["write"]!.AsObject(),
            "bases[1]" => policy["bases"]![1]!.AsObject(),
            "bases[1].read" => policy["bases"]![1]!["read"]!.AsObject(),
            _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, "Not a member of the valid policy."),
        };
    }

    private static string PathOf(string owner, string member)
    {
        return owner.Length == 0 ? member : $"{owner}.{member}";
    }

    // The text of the policy with the escaped string in place of the string Placeholder, which a test puts where the
    // escape goes, as a name or as a value. JsonNode cannot write an escaped lone surrogate, so the text is changed by
    // hand. In C#, "\\uD83D" is the escape of JSON, and "\uD83D" the surrogate itself.
    private static string WithEscapedString(JsonObject policy, string escaped)
    {
        return policy.ToJsonString().Replace($"\"{Placeholder}\"", $"\"{escaped}\"", StringComparison.Ordinal);
    }

    // The error names the member, then says what is wrong there and what is expected, without pinning the words: a
    // reason that names no expectation fails here.
    private static void AssertSaysWhereAndWhat(AgentPolicyException exception, string path)
    {
        Assert.Equal(path, exception.Path);
        Assert.Matches($"^{Regex.Escape(path)}: .+; expected .+", exception.Message);
    }
}
