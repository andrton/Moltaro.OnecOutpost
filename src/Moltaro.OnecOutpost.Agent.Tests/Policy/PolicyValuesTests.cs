using Moltaro.OnecOutpost.Agent.Policy;

namespace Moltaro.OnecOutpost.Agent.Tests.Policy;

// What a valid policy.json gives: the values it names, and the defaults of the members it leaves out.
public sealed class PolicyValuesTests
{
    [Fact]
    public void The_example_of_the_contract_gives_the_policy_it_describes()
    {
        const string Json = """
            {
              "auditRetentionDays": 90,
              "proxy": "http://proxy.local:3128",
              "retryDelayMax": "00:00:30",
              "bases": [
                {
                  "key": "accounting",
                  "server": "srv-1c:1541",
                  "database": "accounting",
                  "read": { "account": "outpost-read", "workers": 1, "runLimit": "00:10:00" },
                  "write": { "account": "outpost-write", "workers": 1, "runLimit": "00:10:00" }
                },
                {
                  "key": "warehouse-file",
                  "file": "D:\\1C\\Warehouse",
                  "read": { "account": "outpost-read" }
                }
              ]
            }
            """;

        AgentPolicy policy = AgentPolicyReader.Read(Json);

        Assert.Equal(90, policy.AuditRetentionDays);
        Assert.Equal(new Uri("http://proxy.local:3128"), policy.Proxy);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.RetryDelayMax);
        Assert.Equal(2, policy.Bases.Count);

        AgentBasePolicy cluster = policy.Bases[0];
        Assert.Equal("accounting", cluster.Key);
        Assert.Equal("srv-1c:1541", cluster.Server);
        Assert.Equal("accounting", cluster.Database);
        Assert.Null(cluster.File);
        Assert.Equal(new AgentModePolicy("outpost-read", 1, TimeSpan.FromMinutes(10)), cluster.Read);
        Assert.Equal(new AgentModePolicy("outpost-write", 1, TimeSpan.FromMinutes(10)), cluster.Write);

        AgentBasePolicy file = policy.Bases[1];
        Assert.Equal("warehouse-file", file.Key);
        Assert.Null(file.Server);
        Assert.Null(file.Database);
        Assert.Equal(@"D:\1C\Warehouse", file.File);
        Assert.Equal(new AgentModePolicy("outpost-read", 1, TimeSpan.FromMinutes(10)), file.Read);
        Assert.Null(file.Write);
    }

    [Fact]
    public void Values_other_than_the_defaults_are_kept()
    {
        const string Json = """
            {
              "auditRetentionDays": 7,
              "retryDelayMax": "00:05:00",
              "bases": [
                {
                  "key": "accounting",
                  "server": "srv-1c",
                  "database": "accounting",
                  "read": { "account": "outpost-read", "workers": 4, "runLimit": "00:45:00" },
                  "write": { "account": "outpost-write", "workers": 2, "runLimit": "02:30:00" }
                }
              ]
            }
            """;

        AgentPolicy policy = AgentPolicyReader.Read(Json);

        Assert.Equal(7, policy.AuditRetentionDays);
        Assert.Equal(TimeSpan.FromMinutes(5), policy.RetryDelayMax);
        Assert.Equal(new AgentModePolicy("outpost-read", 4, TimeSpan.FromMinutes(45)), policy.Bases[0].Read);
        Assert.Equal(new AgentModePolicy("outpost-write", 2, TimeSpan.FromMinutes(150)), policy.Bases[0].Write);
    }

    [Fact]
    public void Members_left_out_take_their_defaults()
    {
        const string Json = """
            {
              "bases": [
                {
                  "key": "accounting",
                  "file": "C:\\1C\\Accounting",
                  "read": { "account": "outpost-read" }
                }
              ]
            }
            """;

        AgentPolicy policy = AgentPolicyReader.Read(Json);

        Assert.Equal(90, policy.AuditRetentionDays);
        Assert.Null(policy.Proxy);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.RetryDelayMax);
        Assert.Equal(new AgentModePolicy("outpost-read", 1, TimeSpan.FromMinutes(10)), policy.Bases[0].Read);
        Assert.Null(policy.Bases[0].Write);
    }
}
