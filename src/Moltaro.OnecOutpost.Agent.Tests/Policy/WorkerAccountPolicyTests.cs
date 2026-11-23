using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Policy;

// The worker account of a base whose Writes run without safe mode (D12, the S9c brief, "write.workerAccount"): the
// local Windows account that runs every COM worker of the base, read into WorkerAccount of its write mode and null
// everywhere else. It is required when safeMode is false, refused when safeMode is true or absent, and an unknown member
// of read. It is 1 to 20 characters, none of " / \ [ ] : ; | = , + * ? < > @ or a control character, and not only
// periods and spaces, so never a domain account or a UPN; and no two bases name one account, ignoring case, so that the
// scripts of one base cannot reach the workers of another. Every error says where and what (Q1) and quotes no value.
public sealed class WorkerAccountPolicyTests
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_write_without_safe_mode_names_its_worker_account_and_every_other_mode_names_none()
    {
        JsonObject policy = Policy(
            Cluster("accounting", Write(safeMode: false, workerAccount: "outpost-worker")),
            Cluster("warehouse", Write(safeMode: true, workerAccount: null)),
            Folder("stock"));

        AgentPolicy read = AgentPolicyReader.Read(policy.ToJsonString());

        Assert.Equal(
            new AgentModePolicy("outpost-write", 1, TimeSpan.FromMinutes(10), SafeMode: false, WorkerAccount: "outpost-worker"),
            read.Bases[0].Write);
        Assert.Null(read.Bases[0].Read.WorkerAccount);
        Assert.Null(read.Bases[1].Write!.WorkerAccount);
        Assert.Null(read.Bases[1].Read.WorkerAccount);
        Assert.Null(read.Bases[2].Read.WorkerAccount);
    }

    // The members of an object may come in any order: the worker account before safeMode is still one of a write
    // without safe mode.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_worker_account_given_before_safe_mode_is_read_as_well()
    {
        const string Json = """
            {
              "bases": [
                {
                  "key": "accounting",
                  "file": "C:\\1C\\Accounting",
                  "read": { "account": "outpost-read" },
                  "write": { "account": "outpost-write", "workerAccount": "outpost-worker", "safeMode": false }
                }
              ]
            }
            """;

        AgentPolicy policy = AgentPolicyReader.Read(Json);

        Assert.Equal("outpost-worker", policy.Bases[0].Write!.WorkerAccount);
    }

    // Each at an edge of the form: one character, twenty, periods and spaces among other characters, letters beyond
    // ASCII, and characters that Windows allows in a name.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("w")]
    [InlineData("outpost-worker-12345")]
    [InlineData("outpost.worker 1")]
    [InlineData(". worker")]
    [InlineData("\u0420\u0430\u0431\u043e\u0442\u043d\u0438\u043a")]
    [InlineData("o'brien_(1)$#!%&~")]
    public void A_worker_account_in_the_form_of_a_local_account_is_kept_as_given(string account)
    {
        JsonObject policy = Policy(Cluster("accounting", Write(safeMode: false, workerAccount: account)));

        AgentPolicy read = AgentPolicyReader.Read(policy.ToJsonString());

        Assert.Equal(account, read.Bases[0].Write!.WorkerAccount);
    }

    // Each breaks the form, and holds S3cret where it holds anything, which the error must not quote: 21 characters,
    // empty, only white space, only periods and spaces, each character that a local account cannot hold, a domain
    // account, a UPN and control characters.
    [Theory]
    [InlineData("S3cret-worker-1234567", "S3cret")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("...", null)]
    [InlineData(". .", null)]
    [InlineData("S3cret\"x", "S3cret")]
    [InlineData("S3cret/x", "S3cret")]
    [InlineData("CORP\\S3cret", "S3cret")]
    [InlineData("S3cret[x", "S3cret")]
    [InlineData("S3cret]x", "S3cret")]
    [InlineData("S3cret:x", "S3cret")]
    [InlineData("S3cret;x", "S3cret")]
    [InlineData("S3cret|x", "S3cret")]
    [InlineData("S3cret=x", "S3cret")]
    [InlineData("S3cret,x", "S3cret")]
    [InlineData("S3cret+x", "S3cret")]
    [InlineData("S3cret*x", "S3cret")]
    [InlineData("S3cret?x", "S3cret")]
    [InlineData("S3cret<x", "S3cret")]
    [InlineData("S3cret>x", "S3cret")]
    [InlineData("S3cret@corp.example", "S3cret")]
    [InlineData("S3cret\u0001x", "S3cret")]
    [InlineData("S3cret\tx", "S3cret")]
    [InlineData("S3cret\u001Fx", "S3cret")]
    public void A_worker_account_that_breaks_the_form_of_a_local_account_is_refused_at_its_path_without_quoting_it(
        string account,
        string? secret)
    {
        JsonObject policy = Policy(Cluster("accounting", Write(safeMode: false, workerAccount: account)));

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        AssertSaysWhereAndWhat(exception, "bases[0].write.workerAccount");

        if (secret is not null)
        {
            Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[\"outpost-worker\"]")]
    public void A_worker_account_that_is_not_a_string_is_refused_at_its_path(string value)
    {
        JsonObject write = Write(safeMode: false, workerAccount: null);
        write["workerAccount"] = JsonNode.Parse(value);

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(Policy(Cluster("accounting", write)).ToJsonString()));

        AssertSaysWhereAndWhat(exception, "bases[0].write.workerAccount");
    }

    // The error of the brief, word for word.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_write_without_safe_mode_and_without_a_worker_account_is_refused_at_its_write()
    {
        const string Expected = """
            bases[0].write: "safeMode" is false without "workerAccount"; expected the name of a local Windows account that runs the workers of the base
            """;

        JsonObject policy = Policy(Cluster("accounting", Write(safeMode: false, workerAccount: null)));

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        Assert.Equal("bases[0].write", exception.Path);
        Assert.Equal(Expected, exception.Message);
    }

    // The error of the brief, word for word, whether safeMode is true or absent, and without the account.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("true")]
    [InlineData("absent")]
    public void A_worker_account_given_while_safe_mode_is_on_is_refused_without_quoting_it(string safeMode)
    {
        const string Expected = """
            bases[0].write.workerAccount: given while "safeMode" is true; remove it, or set "safeMode" to false
            """;

        JsonObject write = Write(safeMode: safeMode == "true" ? true : null, workerAccount: "S3cret-worker");

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(Policy(Cluster("accounting", write)).ToJsonString()));

        Assert.Equal("bases[0].write.workerAccount", exception.Path);
        Assert.Equal(Expected, exception.Message);
    }

    // A Read always runs in safe mode, under the account of its base's workers when the base has one, so read has no
    // worker account of its own.
    [Fact]
    public void A_worker_account_in_read_is_an_unknown_member()
    {
        JsonObject base0 = Cluster("accounting", Write(safeMode: false, workerAccount: "outpost-worker"));
        base0["read"]!["workerAccount"] = "outpost-reader";

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(Policy(base0).ToJsonString()));

        AssertSaysWhereAndWhat(exception, "bases[0].read.workerAccount");
        Assert.DoesNotContain("outpost-reader", exception.Message, StringComparison.Ordinal);
    }

    // The error of the brief, word for word: the third base names the account of the first in another case, with a base
    // of another account between them. It names the earlier base, never the account.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_worker_account_that_an_earlier_base_names_ignoring_case_is_refused_naming_that_base_not_the_account()
    {
        const string Expected = """
            bases[2].write.workerAccount: the account of bases[0] again; expected an account of its own for each base
            """;

        JsonObject policy = Policy(
            Cluster("accounting", Write(safeMode: false, workerAccount: "Outpost-Worker")),
            Cluster("warehouse", Write(safeMode: false, workerAccount: "outpost-warehouse")),
            Cluster("payroll", Write(safeMode: false, workerAccount: "OUTPOST-WORKER")));

        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(
            () => AgentPolicyReader.Read(policy.ToJsonString()));

        Assert.Equal("bases[2].write.workerAccount", exception.Path);
        Assert.Equal(Expected, exception.Message);
    }

    // Accounts that differ only beyond the case are accounts of their own.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void Two_bases_with_worker_accounts_of_their_own_are_valid()
    {
        JsonObject policy = Policy(
            Cluster("accounting", Write(safeMode: false, workerAccount: "outpost-worker-1")),
            Cluster("warehouse", Write(safeMode: false, workerAccount: "outpost-worker-2")));

        AgentPolicy read = AgentPolicyReader.Read(policy.ToJsonString());

        Assert.Equal(["outpost-worker-1", "outpost-worker-2"], read.Bases.Select(basePolicy => basePolicy.Write!.WorkerAccount));
    }

    private static JsonObject Policy(params JsonObject[] bases)
    {
        return new JsonObject { ["bases"] = new JsonArray([.. bases]) };
    }

    // A base on a cluster, read and written with this write.
    private static JsonObject Cluster(string key, JsonObject write)
    {
        return new JsonObject
        {
            ["key"] = key,
            ["server"] = "srv-1c:1541",
            ["database"] = key,
            ["read"] = new JsonObject { ["account"] = "outpost-read" },
            ["write"] = write,
        };
    }

    // A base in a folder that is only read.
    private static JsonObject Folder(string key)
    {
        return new JsonObject
        {
            ["key"] = key,
            ["file"] = $@"D:\1C\{key}",
            ["read"] = new JsonObject { ["account"] = "outpost-read" },
        };
    }

    // A write with safeMode and workerAccount as given, each left out when null.
    private static JsonObject Write(bool? safeMode, string? workerAccount)
    {
        JsonObject write = new() { ["account"] = "outpost-write" };

        if (safeMode is bool given)
        {
            write["safeMode"] = given;
        }

        if (workerAccount is not null)
        {
            write["workerAccount"] = workerAccount;
        }

        return write;
    }

    // The error names the member, then says what is wrong there and what is expected, as PolicyRuleTests states it.
    private static void AssertSaysWhereAndWhat(AgentPolicyException exception, string path)
    {
        Assert.Equal(path, exception.Path);
        Assert.Matches($"^{Regex.Escape(path)}: .+; expected .+", exception.Message);
    }
}
