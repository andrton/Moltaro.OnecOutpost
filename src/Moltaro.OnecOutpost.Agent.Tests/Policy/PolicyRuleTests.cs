using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Agent.Policy;

namespace Moltaro.OnecOutpost.Agent.Tests.Policy;

// The rules of policy.json. A file that breaks any of them is invalid as a whole, and the error names the path of the
// member at fault. Each test changes one member of a valid policy: the owner of the member is given by its path, with
// "" for the root.
public sealed class PolicyRuleTests
{
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
    [InlineData("bases[0].read", "account", "42")]
    [InlineData("bases[0].read", "workers", "0")]
    [InlineData("bases[0].read", "workers", "9")]
    [InlineData("bases[0].read", "workers", "\"1\"")]
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

        Assert.Equal(PathOf(owner, member), exception.Path);
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

        Assert.Equal(PathOf(owner, member), exception.Path);
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

        Assert.Equal(PathOf(owner, member), exception.Path);
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
    }

    // Each text would be a valid policy if comments were allowed or the text were complete.
    [Theory]
    [InlineData("""{ "bases": [ { "key": "a", "file": "C:\\1C\\A", "read": { "account": "r" } } ] } // the bases""")]
    [InlineData("""{ "bases": [ { "key": "a", "file": "C:\\1C\\A", "read": { "account": "r" } } ]""")]
    public void A_file_that_is_not_plain_JSON_makes_the_policy_invalid(string json)
    {
        Assert.Throws<AgentPolicyException>(() => AgentPolicyReader.Read(json));
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
}
