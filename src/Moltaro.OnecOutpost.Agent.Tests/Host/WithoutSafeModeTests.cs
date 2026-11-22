using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Host;

// The limitation of S9b, "Writes without safe mode": the agent starts its COM workers under its own account, so a base
// whose write turns safe mode off is not served for Write until S9c. The host gives the pool, the checks and the loop
// the policy without that write: no worker of the mode starts, the polls declare its write as null (§6.2 of
// docs/protocol.md), and a Write that reaches the agent is Refused not-served, while the base is read as before. The
// start records the base in started.withoutSafeMode.
public sealed class WithoutSafeModeTests
{
    // PairedFolder's policy, with the Writes of accounting out of safe mode.
    private const string Policy = """
        {
          "bases": [
            {
              "key": "accounting",
              "server": "srv-1c:1541",
              "database": "accounting",
              "read": { "account": "outpost-read" },
              "write": { "account": "outpost-write", "safeMode": false }
            },
            {
              "key": "warehouse",
              "file": "D:\\1C\\Warehouse",
              "read": { "account": "outpost-read" }
            }
          ]
        }
        """;

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_base_whose_writes_turn_safe_mode_off_starts_no_write_worker_and_its_polls_declare_its_write_as_null()
    {
        PairedFolder folder = PairedFolder.Create();
        folder.Data.Root.Seed("policy.json", Policy);
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        OutpostPollRequest poll = (await host.NextPollAsync()).ReadPoll();

        OutpostPollBase accounting = poll.Bases.Single(served => served.Key == "accounting");
        Assert.Null(accounting.Write);
        Assert.Equal(1, accounting.Read.Free);
        Assert.DoesNotContain(Operations.Since(host.Data, 0), operation => operation.StartsWith("start accounting write", StringComparison.Ordinal));
        JsonElement started = AuditEntries.AppendedSince(host.Data, 0).Single(entry => AuditEntries.Kind(entry) == "started");
        Assert.Equal(["accounting"], started.GetProperty("withoutSafeMode").EnumerateArray().Select(key => key.GetString()));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_write_to_a_base_whose_writes_turn_safe_mode_off_is_refused_with_not_served_and_its_read_runs()
    {
        PairedFolder folder = PairedFolder.Create();
        folder.Data.Root.Seed("policy.json", Policy);
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        OutpostCommandMessage write = TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Write, TestTimes.Start);
        OutpostCommandMessage read = TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, TestTimes.Start);

        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary(host.Server.Sign(write), host.Server.Sign(read)));
        await host.NextPollAsync();
        await host.Data.WaitForAsync(change => change.Line == $"replace {Operations.OutboxFile(write.Id)}");

        byte[] outcome = host.Data.Changes.Last(change => change.Line == $"replace {Operations.OutboxFile(write.Id)}").Bytes;
        Assert.Equal(TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Refused, OutpostOutcomeCodes.NotServed), outcome);
        JsonElement refused = Assert.Single(AuditEntries.AppendedSince(host.Data, 0), entry => AuditEntries.Kind(entry) == "refused");
        Assert.Equal(write.Id.ToString("D"), refused.GetProperty("command").GetString());
        Assert.Equal(OutpostOutcomeCodes.NotServed, refused.GetProperty("code").GetString());
        List<string> operations = Operations.Since(host.Data, 0);
        Assert.DoesNotContain(operations, operation => operation.StartsWith("run accounting write", StringComparison.Ordinal));
        Operations.AssertInOrder(operations, $"received {read.Id}", "run accounting read 1");
    }
}
