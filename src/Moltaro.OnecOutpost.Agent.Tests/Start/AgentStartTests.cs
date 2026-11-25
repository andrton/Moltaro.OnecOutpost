using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeWorkers;
using static Moltaro.OnecOutpost.Agent.Tests.AuditEntries;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Start;

// What a start finds in the data folder, after a restart included: the audit log,
// whose IOException passes and records nothing; a fault of the log; the access lists; agent.json and secrets.dat; the
// outbox, the records of commands and the poll numbers; and policy.json, the first step that fails deciding. A start
// that passes records started and a valid policy, recovers what the restart left, and returns the run. The access
// lists and the policy have tests of their own.
public sealed class AgentStartTests
{
    private static readonly DateTimeOffset Before = TestTimes.Start.AddDays(-1);

    private readonly PairedFolder _folder = PairedFolder.Create();

    private readonly FakeClock _clock = new(TestTimes.Start);

    // A restart: the Write received before it has no outcome, and becomes Unknown agent-stopped (rule 3).
    [Fact]
    public void A_start_that_passes_records_started_and_a_valid_policy_then_recovers_and_returns_the_run()
    {
        Guid write = Guid.NewGuid();
        _folder.Data.Audit.Seed(FileOf(Before), Received(Before, write, OutpostModeEnum.Write));
        int count = _folder.Data.Changes.Count;

        AgentRun run = _folder.Start(_clock);

        Assert.Equal(
            [
                "append audit\\2026-11-22.jsonl",
                "append audit\\2026-11-22.jsonl",
                $"replace outcomes\\{write}.json",
                "append audit\\2026-11-22.jsonl",
            ],
            _folder.Data.LinesSince(count));

        List<JsonElement> entries = AppendedSince(_folder.Data, count);
        Assert.Equal(["started", "policy", "ended"], entries.Select(Kind));
        Assert.Equal(PairedFolder.Version, entries[0].GetProperty("version").GetString());
        Assert.Empty(entries[0].GetProperty("withoutSafeMode").EnumerateArray());
        AssertNone(entries[0], "error");
        Assert.True(entries[1].GetProperty("valid").GetBoolean());
        AssertNone(entries[1], "error");
        Assert.Equal("agent-stopped", entries[2].GetProperty("code").GetString());

        Assert.Equal(["accounting", "warehouse"], run.Policy.Bases.Select(basePolicy => basePolicy.Key));
        Assert.Equal(new Uri("https://outpost.example.com/agents"), run.Pairing.Server);
        Assert.Equal(PairedFolder.Agent, run.Pairing.Agent);
        Assert.Equal(_folder.ServerKey, run.Pairing.ServerKey);
        Assert.Equal(TimeSpan.FromSeconds(25), run.Pairing.PollTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), run.Pairing.LeaseGrace);
        Assert.Equal(_folder.PrivateKey, run.Secrets.PrivateKey);
        Assert.Equal("write-pw", run.Secrets.Passwords[("accounting", OutpostModeEnum.Write)]);
        Assert.Equal("", run.Secrets.Passwords[("warehouse", OutpostModeEnum.Read)]);
        Assert.Equal([write], run.Outbox.Commands);
        Assert.Equal(1, run.Numbers.Next());

        run.Log.Dispose();
    }

    // A second copy of the agent gets IOException from the lock and records nothing. The program exits with an error.
    // Disposing the log of the run releases the lock.
    [Fact]
    public void A_second_start_while_the_first_runs_fails_with_IOException_and_records_nothing()
    {
        AgentRun first = _folder.Start(_clock);
        int count = _folder.Data.Changes.Count;

        Assert.ThrowsAny<IOException>(() => _folder.Start(_clock));
        Assert.Empty(_folder.Data.LinesSince(count));

        first.Log.Dispose();

        _folder.Start(_clock).Log.Dispose();
    }

    // Any line that the log cannot read, and an audit file that it cannot open, stop the agent
    // with started and an error.
    [Theory]
    [InlineData("a line that cannot be read")]
    [InlineData("a file that cannot be opened")]
    public void A_log_that_cannot_be_read_stops_the_start_with_started_and_an_error(string fault)
    {
        if (fault == "a line that cannot be read")
        {
            _folder.Data.Audit.Seed(FileOf(Before), Connection(Before) + "not an entry\n" + Connection(Before));
        }
        else
        {
            _folder.Data.Audit.Seed(FileOf(Before), Connection(Before));
            _folder.Data.Audit.FailOpenRead(FileOf(Before));
        }

        List<JsonElement> entries = StoppedStart.Of(_folder, _clock);

        Assert.Single(entries);
        Assert.Contains(FileOf(Before), StoppedStart.Error(entries), StringComparison.Ordinal);
    }

    // A last line without its LF, and a last line that is not JSON, are cut at a start, and the
    // agent then starts normally twice in a row. The torn line is in the file of the day that started goes to.
    [Theory]
    [InlineData("{\"time\":\"2026-11-22T09:00:00.000Z\",\"entry\":\"conn")]
    [InlineData("{\"time\":\"2026-11-22T09:00:00.000Z\",\"entry\":\"conn\n")]
    public void A_torn_last_line_is_cut_and_the_agent_then_starts_normally_twice_in_a_row(string tornLine)
    {
        string kept = Connection(TestTimes.Start.AddHours(-1));
        _folder.Data.Audit.Seed("2026-11-22.jsonl", kept + tornLine);
        int count = _folder.Data.Changes.Count;

        _folder.Start(_clock).Log.Dispose();

        Assert.Equal($"cut audit\\2026-11-22.jsonl to {Encoding.UTF8.GetByteCount(kept)}", _folder.Data.LinesSince(count)[0]);

        count = _folder.Data.Changes.Count;
        _folder.Start(_clock).Log.Dispose();

        Assert.Equal(["started", "policy"], KindsSince(_folder.Data, count));
        Assert.DoesNotContain(_folder.Data.LinesSince(count), line => line.StartsWith("cut ", StringComparison.Ordinal));
    }

    // An absent agent.json says that the agent is not paired. No reason quotes what the file holds.
    [Theory]
    [InlineData("agent.json", "absent")]
    [InlineData("agent.json", "unreadable")]
    [InlineData("agent.json", "malformed")]
    [InlineData("secrets.dat", "absent")]
    [InlineData("secrets.dat", "unreadable")]
    [InlineData("secrets.dat", "malformed")]
    [InlineData("secrets.dat", "refused by the protection")]
    public void A_pairing_or_secrets_file_that_cannot_be_read_stops_the_start_naming_the_file(string file, string fault)
    {
        switch (fault)
        {
            case "absent":
                _folder.Data.Root.Unseed(file);
                break;

            case "unreadable":
                _folder.Data.Root.FailOpenRead(file);
                break;

            case "malformed" when file == "agent.json":
                _folder.Data.Root.Seed(file, """{"server":"https://S3cret.example.com/agents"}""");
                break;

            case "malformed":
                _folder.Data.Root.Seed(file, _folder.Protection.Protect("S3cret is not JSON"u8.ToArray()));
                break;

            default:
                _folder.Protection.Refuses = true;
                break;
        }

        string error = StoppedStart.Error(StoppedStart.Of(_folder, _clock));

        Assert.Contains(file, error, StringComparison.Ordinal);
        Assert.DoesNotContain("S3cret", error, StringComparison.Ordinal);

        if (file == "agent.json" && fault == "absent")
        {
            Assert.Contains("not paired", error, StringComparison.OrdinalIgnoreCase);
        }
    }

    // An outbox file that cannot be read, and an ended with neither its file nor its confirmed,
    // stop the agent with started and an error; so do a file that no entry names, a name the outbox does not know, and
    // poll numbers used up.
    [Theory]
    [InlineData("an outbox file that cannot be read")]
    [InlineData("an ended with neither its file nor its confirmed")]
    [InlineData("an outbox file of a command that no entry names")]
    [InlineData("an outbox file of another name")]
    [InlineData("a reservation above the greatest number")]
    public void What_the_outbox_the_records_or_the_poll_numbers_cannot_go_on_from_stops_the_start(string state)
    {
        Guid command = Guid.NewGuid();
        string ended = Received(Before, command, OutpostModeEnum.Write) + Ended(Before, command, "succeeded", null, 5);

        switch (state)
        {
            case "an outbox file that cannot be read":
                _folder.Data.Audit.Seed(FileOf(Before), ended);
                _folder.Data.Outcomes.Seed($"{command}.json", TestOutcomes.Succeeded());
                _folder.Data.Outcomes.FailOpenRead($"{command}.json");
                break;

            case "an ended with neither its file nor its confirmed":
                _folder.Data.Audit.Seed(FileOf(Before), ended);
                break;

            case "an outbox file of a command that no entry names":
                _folder.Data.Outcomes.Seed($"{command}.json", TestOutcomes.Succeeded());
                break;

            case "an outbox file of another name":
                _folder.Data.Outcomes.Seed("planted.json", TestOutcomes.Succeeded());
                break;

            default:
                _folder.Data.Audit.Seed(
                    FileOf(Before),
                    Reservation(Before, PairedFolder.Agent, OutpostProtocol.MaxPollNumber + 1));
                break;
        }

        Assert.Single(StoppedStart.Of(_folder, _clock));
    }

    // The order of the steps: the access lists come before agent.json, and agent.json before policy.json.
    [Fact]
    public void The_first_step_that_fails_decides()
    {
        _folder.Access.Admit("", @"PC-1\intruder");
        _folder.Data.Root.Unseed("agent.json");
        _folder.Data.Root.Seed("policy.json", """{ "bases": [] }""");

        List<JsonElement> entries = StoppedStart.Of(_folder, _clock);

        Assert.Single(entries);
        Assert.Contains(@"PC-1\intruder", StoppedStart.Error(entries), StringComparison.Ordinal);
        Assert.DoesNotContain("agent.json", StoppedStart.Error(entries), StringComparison.Ordinal);

        _folder.Access.Fix();
        entries = StoppedStart.Of(_folder, _clock);

        Assert.Single(entries);
        Assert.Contains("agent.json", StoppedStart.Error(entries), StringComparison.Ordinal);
    }
}
