using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Host;

// The entry stopped of the audit log (the owner's Q16; the S10 brief, "Internal of S10c", "The amendment for Q16"),
// through the fakes of the disk: when the loop stops the agent for a fault after its started, AgentHost.RunAsync
// records {"time","entry":"stopped","error"} with the reason, worded where it is thrown, which names a file by its path
// and never a script, a parameter, a result or a secret, and then exits with Error, as before. The faults: an outbox
// file that cannot be written, read or removed; a cleared log, which the server answers as older; and the poll numbers
// used up. A stop by the token writes no stopped; after a failed write of the log nothing more is written, stopped
// neither; and the next start reads a log that ends with stopped.
public sealed class StoppedEntryTests
{
    // What a command and its result carry, none of which a stopped may hold.
    private const string Script = "Result = \"the script of the command\";";

    private const string Parameter = "a parameter of the command";

    private const string Result = "a result of the command";

    // The executor's outcome of the command, with its result and a message of its own.
    private static readonly byte[] Outcome = Encoding.UTF8.GetBytes(
        $$"""{ "kind": "succeeded", "code": null, "result": { "customer": "{{Result}}" }, "errorText": null, "messages": [ "{{Result}}" ], "messagesTrimmed": false }""");

    // The outbox file of the command cannot be written when its outcome is recorded, read when it is sent, or removed
    // once the server confirmed it.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("written")]
    [InlineData("read")]
    [InlineData("removed")]
    public async Task An_outbox_file_that_cannot_be_used_during_the_run_is_recorded_as_stopped_with_its_path_and_the_agent_exits_with_Error(
        string fault)
    {
        PairedFolder folder = PairedFolder.Create();
        FakeWorkerLauncher workers = new(folder.Data) { Runs = _ => FakeRun.Gives(Outcome) };
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start), workers);
        OutpostCommandMessage read = TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, TestTimes.Start, Script, $$"""{"invoice":"{{Parameter}}"}""");
        string file = $"{read.Id}.json";

        switch (fault)
        {
            case "written":
                folder.Data.Outcomes.FailReplace(file);
                break;
            case "read":
                folder.Data.Outcomes.FailOpenRead(file);
                break;
            default:
                folder.Data.Outcomes.FailDelete(file);
                break;
        }

        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary(host.Server.Sign(read)));

        if (fault == "removed")
        {
            (await host.NextOutcomeAsync()).Confirm();
        }

        Assert.Equal(AgentExitCodes.Error, await host.ExitAsync());
        AssertStopped(host, $@"outcomes\{read.Id}.json cannot be {fault}", Script, Parameter, Result);
    }

    // The log holds no reservation of the agent, so its first poll is counted; a server that answers it as older has
    // recorded polls that the log does not hold.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_cleared_log_that_the_server_answers_as_older_is_recorded_as_stopped_after_its_reservation_and_the_agent_exits_with_Error()
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        (await host.NextPollAsync()).AnswerPoll(host.Server.Older(5));

        Assert.Equal(AgentExitCodes.Error, await host.ExitAsync());
        AssertStopped(
            host,
            "the server has recorded polls of this agent that its audit log does not hold: the log was cleared; pair the agent anew");
        Assert.Equal("reservation", AuditEntries.Kind(AuditEntries.AppendedSince(host.Data, 0)[^2]));
    }

    // The next number would pass 2⁶²: the agent records its reservation, then stopped.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Poll_numbers_used_up_are_recorded_as_stopped_after_their_reservation_and_the_agent_exits_with_Error()
    {
        PairedFolder folder = PairedFolder.Create();
        EarlierRun.Append(folder, Reservation(EarlierRun.Time, PairedFolder.Agent, TestTimes.Start.UtcTicks));
        DateTimeOffset now = TestTimes.Start;
        await using TestHost host = TestHost.Start(folder, new FakeClock(now));

        (await host.NextPollAsync()).AnswerPoll(host.Server.Older(OutpostProtocol.MaxPollNumber));

        // The number is taken for the next poll, after the wait that an older poll's answer gives.
        Task delay = host.WaitForTimerAsync(now + TimeSpan.FromSeconds(3));

        if (await Task.WhenAny(host.Exit, delay) == delay)
        {
            host.Clock.Advance(TimeSpan.FromSeconds(3));
        }

        Assert.Equal(AgentExitCodes.Error, await host.ExitAsync());
        AssertStopped(host, "the poll numbers are used up; pair the agent anew");
        Assert.Equal("reservation", AuditEntries.Kind(AuditEntries.AppendedSince(host.Data, 0)[^2]));
    }

    // A normal stop of the service is no fault.
    [Fact]
    public async Task A_stop_by_the_token_writes_no_stopped()
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        await host.NextPollAsync();

        Assert.Equal(AgentExitCodes.Stopped, await host.StopAsync());

        Assert.DoesNotContain("stopped", AuditEntries.KindsSince(host.Data, 0));
    }

    // The write of a received fails, as the disk refuses it: the log cuts it back, and nothing more is written, stopped
    // neither; the exit code is the reason.
    [Fact]
    public async Task A_failed_write_of_the_log_writes_nothing_after_it_stopped_neither()
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        OutpostCommandMessage read = TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, TestTimes.Start);

        // The first answer records connected and ends the counting; the second has nothing to record before the
        // command.
        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary());
        ServerExchange second = await host.NextPollAsync();
        int count = host.Data.Changes.Count;
        host.Data.Audit.FailNextWrite(bytesKept: 10);
        second.AnswerPoll(host.Server.Ordinary(host.Server.Sign(read)));

        Assert.Equal(AgentExitCodes.Error, await host.ExitAsync());

        List<FolderChange> after = [.. host.Data.Changes.Skip(count)];
        int failed = after.FindIndex(change => change.Line.StartsWith(@"append audit\", StringComparison.Ordinal));
        Assert.True(failed >= 0, $"No write of the log after the fault: {string.Join(" | ", after)}");
        Assert.DoesNotContain(after.Skip(failed + 1), change => change.Line.StartsWith(@"append audit\", StringComparison.Ordinal));
        Assert.All(
            host.Data.Audit.Names().Where(name => name.EndsWith(".jsonl", StringComparison.Ordinal)),
            name => Assert.DoesNotContain("\"entry\":\"stopped\"", host.Data.Audit.Text(name), StringComparison.Ordinal));
    }

    // The reader of the log accepts stopped with its string error, so the start after a stop records started without an
    // error and polls.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_start_reads_a_log_that_ends_with_stopped_records_started_without_an_error_and_polls()
    {
        PairedFolder folder = PairedFolder.Create();
        EarlierRun.Append(
            folder,
            Started(EarlierRun.Time) + Stopped(EarlierRun.Time, $@"outcomes\{Guid.NewGuid()}.json cannot be written"));
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        OutpostPollRequest poll = (await host.NextPollAsync()).ReadPoll();

        JsonElement started = AuditEntries.AppendedSince(host.Data, 0)[0];
        Assert.Equal("started", AuditEntries.Kind(started));
        AuditEntries.AssertNone(started, "error");
        Assert.Equal(PairedFolder.Version, poll.AgentVersion);
        Assert.Equal(AgentExitCodes.Stopped, await host.StopAsync());
    }

    // The last entry that the run appended is its one stopped, written by the writer of §2 at the time of the clock,
    // with the reason as its error and no other member, and holding none of the values given nor any password of
    // secrets.dat.
    private static void AssertStopped(TestHost host, string error, params string[] values)
    {
        List<JsonElement> entries = AuditEntries.AppendedSince(host.Data, 0);
        JsonElement stopped = entries[^1];

        Assert.Equal("stopped", AuditEntries.Kind(stopped));
        Assert.Single(entries, entry => AuditEntries.Kind(entry) == "stopped");
        AuditEntries.AssertOnly(stopped, "time", "entry", "error");
        Assert.Equal(Time(host.Clock.GetUtcNow()), stopped.GetProperty("time").GetString());
        Assert.Equal(error, stopped.GetProperty("error").GetString());

        string text = stopped.GetRawText();
        string[] secrets = [.. PairedFolder.Passwords.Select(entry => entry.Password).Where(password => password.Length > 0)];

        foreach (string value in values.Concat(secrets))
        {
            Assert.DoesNotContain(value, text, StringComparison.Ordinal);
        }
    }
}
