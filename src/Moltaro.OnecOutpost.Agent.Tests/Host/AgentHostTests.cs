using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Tests.FakeHttp;
using Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;
using Moltaro.OnecOutpost.Agent.Tests.Loop;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Host;

// The in-process host (the S6 brief, "AgentHost.RunAsync" and "AgentExitCodes"): the start, the keys, the pool with
// its opens started, the connection over the handler of AgentParts.Handlers, the checks and the loop. It gives Stopped
// for its token, a cancellation while workers open included; Error for a stop of the agent's own, recorded when the log
// can be written; and NotStarted when the log was not opened, a second copy included, with nothing recorded. It
// disposes the pool, the connection and the log, in that order.
public sealed class AgentHostTests
{
    // The workers are killed first, then the handler goes while the log still holds its lock, and the lock is free
    // once the run ended.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_run_stopped_by_its_token_gives_Stopped_and_disposes_the_pool_the_connection_and_the_log_in_that_order()
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        await host.NextPollAsync();
        host.Server.Disposing = () => host.Data.Note(LockState(folder));

        Assert.Equal(AgentExitCodes.Stopped, await host.StopAsync());

        List<string> operations = Operations.Since(host.Data, 0);

        foreach (string worker in new[] { "accounting read 1", "accounting write 1", "warehouse read 1" })
        {
            Operations.AssertInOrder(operations, $"kill {worker}", "handler disposed, the log held");
        }

        Assert.True(host.Server.IsDisposed);
        folder.Data.Audit.Lock("lock").Dispose();
    }

    // The workers never answer their opens, so every mode declares free 0.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_run_stopped_while_its_workers_open_gives_Stopped()
    {
        PairedFolder folder = PairedFolder.Create();
        FakeWorkerLauncher workers = new(folder.Data) { Opens = (_, _, _) => FakeOpenEnum.Never };
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start), workers);

        OutpostPollRequest poll = (await host.NextPollAsync()).ReadPoll();

        Assert.All(poll.Bases, served => Assert.Equal(0, served.Read.Free + (served.Write?.Free ?? 0)));
        Assert.Equal(AgentExitCodes.Stopped, await host.StopAsync());
    }

    // §13: the agent holds audit\lock for its whole run, so a second copy of the program exits with an error while the
    // first runs, and writes nothing.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_second_copy_gives_NotStarted_and_records_nothing_while_the_first_runs_on()
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost first = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        ServerExchange poll = await first.NextPollAsync();
        int count = folder.Data.Changes.Count;

        await using TestHost second = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        Assert.Equal(AgentExitCodes.NotStarted, await second.ExitAsync());
        Assert.Empty(folder.Data.LinesSince(count));
        Assert.Equal(0, second.Server.Count(OutpostRequestKindEnum.Poll));

        poll.AnswerPoll(first.Server.Ordinary());
        await first.NextPollAsync();
    }

    // A start that stops records started with its error, and nothing is polled.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_start_that_stops_gives_Error_recorded_and_polls_nothing()
    {
        PairedFolder folder = PairedFolder.Create();
        folder.Data.Root.Unseed("agent.json");
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        Assert.Equal(AgentExitCodes.Error, await host.ExitAsync());

        JsonElement started = Assert.Single(AuditEntries.AppendedSince(host.Data, 0));
        Assert.Equal("started", AuditEntries.Kind(started));
        Assert.Contains("agent.json", started.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, host.Server.Count(OutpostRequestKindEnum.Poll));
    }

    // An invalid policy serves nothing and is recorded: started with the error and policy invalid; its outbox waits on
    // the disk for a start with a valid policy, and nothing is sent.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task An_invalid_policy_serves_nothing_is_recorded_and_its_outbox_waits_on_the_disk()
    {
        PairedFolder folder = PairedFolder.Create();
        Guid waiting = EarlierRun.Ended(folder, OutpostModeEnum.Read, TestOutcomes.Succeeded());
        folder.Data.Root.Seed("policy.json", """{ "bases": [] }""");
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        Assert.Equal(AgentExitCodes.Error, await host.ExitAsync());

        Assert.Equal<string>(["started", "policy"], AuditEntries.KindsSince(host.Data, 0));
        Assert.False(AuditEntries.AppendedSince(host.Data, 0)[1].GetProperty("valid").GetBoolean());
        Assert.Equal(0, host.Server.Count(OutpostRequestKindEnum.Poll));
        Assert.Equal(0, host.Server.Count(OutpostRequestKindEnum.Outcome));
        Assert.True(folder.Data.Outcomes.Has($"{waiting}.json"));
    }

    // Free 0 until S9b: the program's launcher starts no COM worker yet, and what it starts never opens.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task With_the_programs_launcher_every_mode_declares_free_0()
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start), new ComWorkerLauncher());

        OutpostPollRequest poll = (await host.NextPollAsync()).ReadPoll();

        Assert.Equal(2, poll.Bases.Count);
        Assert.All(poll.Bases, served => Assert.Equal(0, served.Read.Free + (served.Write?.Free ?? 0)));
    }

    // Whether the audit log still holds its lock when the handler is disposed: a lock of the test fails while it does.
    private static string LockState(PairedFolder folder)
    {
        try
        {
            folder.Data.Audit.Lock("lock").Dispose();

            return "handler disposed, the log released";
        }
        catch (IOException)
        {
            return "handler disposed, the log held";
        }
    }
}
