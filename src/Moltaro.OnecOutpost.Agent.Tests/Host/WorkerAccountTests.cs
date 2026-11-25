using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;
using Moltaro.OnecOutpost.TestSupport.FakeWorkers;

namespace Moltaro.OnecOutpost.Agent.Tests.Host;

// A base whose write turns safe mode off is served for Write (the S9c brief, in place of S9b's "Writes without safe
// mode"): its polls declare its write, and a Write runs without safe mode. Every COM worker of the base, Reads
// included, runs under the worker account that its policy names, which the start logged on before any worker started;
// the agent's own account runs only the bases without one (concept §3, D12, "The account of a worker"). A logon that
// fails stops the agent before any worker starts. Through TestHost, with S6b's fake launcher, whose processes say the
// account they run under.
public sealed class WorkerAccountTests
{
    // PairedFolder's policy, with the Writes of accounting out of safe mode under the account outpost-worker.
    private static readonly string Policy = PairedFolder.PolicyWithWorkerAccount("outpost-worker");

    private readonly PairedFolder _folder = PairedFolder.Create();

    public WorkerAccountTests()
    {
        _folder.Data.Root.Seed("policy.json", Policy);
        _folder.SeedSecrets(PairedFolder.Passwords, [("accounting", "worker-pw")]);
    }

    [Fact]
    public async Task A_base_whose_write_turns_safe_mode_off_declares_its_write_and_runs_a_write_without_safe_mode()
    {
        FakeWorkerLauncher workers = new(_folder.Data);
        await using TestHost host = TestHost.Start(_folder, new FakeClock(TestTimes.Start), workers);
        OutpostCommandMessage write = TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Write, TestTimes.Start);

        ServerExchange first = await host.NextPollAsync();
        OutpostPollBase accounting = first.ReadPoll().Bases.Single(served => served.Key == "accounting");
        first.AnswerPoll(host.Server.Ordinary(host.Server.Sign(write)));
        await host.NextPollAsync();
        ServerExchange outcome = await host.NextOutcomeAsync();

        Assert.NotNull(accounting.Write);
        Assert.Equal(1, accounting.Write.Free);
        Assert.Equal(write.Id, outcome.ReadOutcome().Command);
        Assert.Equal(TestOutcomes.Succeeded(), outcome.OutcomeBytes());
        Assert.False(workers.Process("accounting", OutpostModeEnum.Write).Connection!.SafeMode);
        Operations.AssertInOrder(Operations.Since(host.Data, 0), $"received {write.Id}", "run accounting write 1", $"ended {write.Id}");

        JsonElement started = AuditEntries.AppendedSince(host.Data, 0).Single(entry => AuditEntries.Kind(entry) == "started");
        Assert.Equal(["accounting"], AuditEntries.Strings(started.GetProperty("withoutSafeMode")));
    }

    // The invariant of the launcher, seen through the fake: the start logs the base on before any worker starts, so
    // its Read workers run under the account as its Write workers do, and warehouse, which names none, runs under the
    // agent's own.
    [Fact]
    public async Task Every_worker_of_a_base_with_a_worker_account_runs_under_it_reads_included_and_no_other_base_does()
    {
        FakeWorkerLauncher workers = new(_folder.Data);
        await using TestHost host = TestHost.Start(_folder, new FakeClock(TestTimes.Start), workers);

        await host.NextPollAsync();

        Assert.Equal([("accounting", "outpost-worker", "worker-pw")], workers.LogOns);
        Assert.Equal("outpost-worker", workers.Process("accounting", OutpostModeEnum.Read).Account);
        Assert.Equal("outpost-worker", workers.Process("accounting", OutpostModeEnum.Write).Account);
        Assert.Null(workers.Process("warehouse", OutpostModeEnum.Read).Account);

        List<string> operations = Operations.Since(host.Data, 0);
        Operations.AssertInOrder(operations, "logon accounting outpost-worker", "start accounting read 1");
        Operations.AssertInOrder(operations, "logon accounting outpost-worker", "start accounting write 1");
        Operations.AssertInOrder(operations, "logon accounting outpost-worker", "start warehouse read 1");
    }

    [Fact]
    public async Task A_logon_that_fails_stops_the_agent_recorded_before_any_worker_starts_and_polls_nothing()
    {
        const string Reason = "the worker account outpost-worker of the base accounting cannot log on: refused by the test";
        FakeWorkerLauncher workers = new(_folder.Data);
        workers.RefuseLogOn("accounting", Reason);
        await using TestHost host = TestHost.Start(_folder, new FakeClock(TestTimes.Start), workers);

        Assert.Equal(AgentExitCodes.Error, await host.ExitAsync());

        JsonElement started = Assert.Single(AuditEntries.AppendedSince(host.Data, 0));
        Assert.Equal("started", AuditEntries.Kind(started));
        Assert.Equal(Reason, started.GetProperty("error").GetString());
        Assert.Empty(workers.Processes);
        Assert.Equal(0, host.Server.Count(OutpostRequestKindEnum.Poll));
    }
}
