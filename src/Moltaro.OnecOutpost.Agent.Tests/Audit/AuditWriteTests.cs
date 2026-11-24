using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;
using static Moltaro.OnecOutpost.Agent.Tests.AuditEntries;

namespace Moltaro.OnecOutpost.Agent.Tests.Audit;

// Writing the audit log (rule 2, §13 of docs/protocol.md, "The audit log" of the plan, the S6a brief): each writer adds
// one entry, one JSON object by the writer of §2 with the time of the clock, the kind of the entry and its members, and
// LF last, with one append to the file of the UTC day of that time, on the disk before the writer returns. A write that
// fails is cut back and stops the agent; with a fault, nothing but started is written.
public sealed class AuditWriteTests
{
    private static readonly Guid Agent = PairedFolder.Agent;

    // Every writer but started.
    private static readonly string[] WriterNames =
        ["policy", "connection", "bases", "received", "refused", "ended", "confirmed", "reservation"];

    private readonly MemoryDataFolder _data = new();

    // A time finer than a millisecond, which an entry truncates (§2).
    private readonly FakeClock _clock = new(TestTimes.Start + TimeSpan.FromTicks(4567));

    public static TheoryData<string> Writers => [.. WriterNames];

    // Each writer adds one entry to the file of the day; ended and policy are stated further in their own tests.
    [Fact]
    public void Each_writer_appends_one_entry_by_the_writer_of_section_2_with_the_time_of_the_clock()
    {
        Guid write = Guid.NewGuid();
        Guid refused = Guid.NewGuid();
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        log.Started("0.1.0", ["accounting", "payroll"], null);
        log.Bases(["accounting"], ["payroll"]);
        log.Received(TestCommands.Of(
            write,
            OutpostModeEnum.Write,
            TestTimes.Start,
            script: "Сообщить(\"Готово\");\n",
            parameters: """{ "number" : "42", "lines" : [ 1, 2 ] }"""));
        log.Refused(refused, "accounting", OutpostModeEnum.Read, "expired");
        log.Refused(null, null, null, "signature");
        log.Confirmed(write);
        log.Reservation(Agent, 639_000_000_000_000_000);

        Assert.All(_data.Changes, change => Assert.Equal("append audit\\2026-11-22.jsonl", change.Line));

        List<JsonElement> entries = AppendedSince(_data, 0);
        Assert.Equal(["started", "bases", "received", "refused", "refused", "confirmed", "reservation"], entries.Select(Kind));
        Assert.All(entries, entry => Assert.Equal("2026-11-22T10:15:00.123Z", entry.GetProperty("time").GetString()));

        JsonElement started = entries[0];
        AssertOnly(started, "time", "entry", "version", "withoutSafeMode", "error");
        Assert.Equal("0.1.0", started.GetProperty("version").GetString());
        Assert.Equal(["accounting", "payroll"], Strings(started.GetProperty("withoutSafeMode")));
        AssertNone(started, "error");

        JsonElement bases = entries[1];
        AssertOnly(bases, "time", "entry", "held", "claimed");
        Assert.Equal(["accounting"], Strings(bases.GetProperty("held")));
        Assert.Equal(["payroll"], Strings(bases.GetProperty("claimed")));

        // The parameters are written again by the writer of §2, not copied: no white space and no escape it does not
        // write.
        JsonElement received = entries[2];
        AssertOnly(received, "time", "entry", "command", "base", "mode", "callerKey", "script", "parameters", "issued", "deadline");
        Assert.Equal(write.ToString(), received.GetProperty("command").GetString());
        Assert.Equal("accounting", received.GetProperty("base").GetString());
        Assert.Equal("write", received.GetProperty("mode").GetString());
        Assert.Equal("invoice-42", received.GetProperty("callerKey").GetString());
        Assert.Equal("Сообщить(\"Готово\");\n", received.GetProperty("script").GetString());
        Assert.Equal("""{"number":"42","lines":[1,2]}""", received.GetProperty("parameters").GetRawText());
        Assert.Equal("2026-11-22T10:15:00.123Z", received.GetProperty("issued").GetString());
        Assert.Equal("2026-11-22T10:15:30.123Z", received.GetProperty("deadline").GetString());

        JsonElement refusedEntry = entries[3];
        AssertOnly(refusedEntry, "time", "entry", "command", "base", "mode", "code");
        Assert.Equal(refused.ToString(), refusedEntry.GetProperty("command").GetString());
        Assert.Equal("accounting", refusedEntry.GetProperty("base").GetString());
        Assert.Equal("read", refusedEntry.GetProperty("mode").GetString());
        Assert.Equal("expired", refusedEntry.GetProperty("code").GetString());

        // Nothing of a command whose signature does not verify can be trusted, so its members are null.
        JsonElement signature = entries[4];
        AssertOnly(signature, "time", "entry", "command", "base", "mode", "code");
        Assert.Equal(JsonValueKind.Null, signature.GetProperty("command").ValueKind);
        Assert.Equal(JsonValueKind.Null, signature.GetProperty("base").ValueKind);
        Assert.Equal(JsonValueKind.Null, signature.GetProperty("mode").ValueKind);
        Assert.Equal("signature", signature.GetProperty("code").GetString());

        JsonElement confirmed = entries[5];
        AssertOnly(confirmed, "time", "entry", "command");
        Assert.Equal(write.ToString(), confirmed.GetProperty("command").GetString());

        JsonElement reservation = entries[6];
        AssertOnly(reservation, "time", "entry", "agent", "below");
        Assert.Equal(Agent.ToString(), reservation.GetProperty("agent").GetString());
        Assert.Equal(639_000_000_000_000_000, reservation.GetProperty("below").GetInt64());
    }

    // policy.valid is true exactly when error is null; connection.state is connected or lost, with the reason of §9;
    // ended.outcome is succeeded, failed or unknown; started carries the error that stops the start.
    [Fact]
    public void The_members_of_started_policy_connection_and_ended_follow_their_table()
    {
        Guid command = Guid.NewGuid();
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        log.Started("0.1.0", [], "policy.json: bases[0].server: the value is empty");
        log.Policy(null);
        log.Policy("bases[0].server: the value is empty");
        log.Connection(true, null);
        log.Connection(false, "status-502");
        log.Ended(command, OutpostAgentOutcomeKindEnum.Succeeded, null, 812);
        log.Ended(command, OutpostAgentOutcomeKindEnum.Failed, "script-error", 0);
        log.Ended(command, OutpostAgentOutcomeKindEnum.Unknown, "agent-stopped", null);

        List<JsonElement> entries = AppendedSince(_data, 0);
        Assert.Equal(
            ["started", "policy", "policy", "connection", "connection", "ended", "ended", "ended"],
            entries.Select(Kind));

        Assert.Equal("policy.json: bases[0].server: the value is empty", entries[0].GetProperty("error").GetString());
        Assert.Empty(entries[0].GetProperty("withoutSafeMode").EnumerateArray());

        AssertOnly(entries[1], "time", "entry", "valid", "error");
        Assert.True(entries[1].GetProperty("valid").GetBoolean());
        AssertNone(entries[1], "error");
        Assert.False(entries[2].GetProperty("valid").GetBoolean());
        Assert.Equal("bases[0].server: the value is empty", entries[2].GetProperty("error").GetString());

        AssertOnly(entries[3], "time", "entry", "state", "reason");
        Assert.Equal("connected", entries[3].GetProperty("state").GetString());
        AssertNone(entries[3], "reason");
        Assert.Equal("lost", entries[4].GetProperty("state").GetString());
        Assert.Equal("status-502", entries[4].GetProperty("reason").GetString());

        foreach (JsonElement ended in entries[5..])
        {
            AssertOnly(ended, "time", "entry", "command", "outcome", "code", "durationMs");
            Assert.Equal(command.ToString(), ended.GetProperty("command").GetString());
        }

        Assert.Equal("succeeded", entries[5].GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, entries[5].GetProperty("code").ValueKind);
        Assert.Equal(812, entries[5].GetProperty("durationMs").GetInt64());
        Assert.Equal("failed", entries[6].GetProperty("outcome").GetString());
        Assert.Equal("script-error", entries[6].GetProperty("code").GetString());
        Assert.Equal(0, entries[6].GetProperty("durationMs").GetInt64());
        Assert.Equal("unknown", entries[7].GetProperty("outcome").GetString());
        Assert.Equal("agent-stopped", entries[7].GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, entries[7].GetProperty("durationMs").ValueKind);
    }

    // The acceptance of S6: no entry is written before the one before it is on the disk. In memory, an append that has
    // returned is on the disk.
    [Fact]
    public void Each_entry_is_appended_whole_before_its_writer_returns()
    {
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        foreach (string writer in WriterNames.Prepend("started"))
        {
            int count = _data.Changes.Count;

            if (writer == "started")
            {
                log.Started("0.1.0", [], null);
            }
            else
            {
                Write(log, writer);
            }

            Assert.Equal(["append audit\\2026-11-22.jsonl"], _data.LinesSince(count));
            Assert.Equal(writer, Kind(WholeEntry(_data.Changes[count].Bytes)));
        }
    }

    [Fact]
    public void An_entry_goes_to_the_file_of_the_UTC_day_of_its_time()
    {
        FakeClock clock = new(new DateTimeOffset(2026, 11, 22, 23, 59, 59, 999, TimeSpan.Zero));
        using AuditLog log = AuditLog.Open(_data.Audit, clock);

        log.Connection(true, null);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        log.Connection(false, "unreachable");

        Assert.Equal(["append audit\\2026-11-22.jsonl", "append audit\\2026-11-23.jsonl"], _data.LinesSince(0));
        Assert.Equal("2026-11-23T00:00:00.000Z", AppendedSince(_data, 0)[1].GetProperty("time").GetString());
    }

    // §13: a write that fails, on a full disk for example, is cut back to the length the file had before it; the
    // next start finds the log as it was.
    [Fact]
    public void A_failed_write_cuts_the_file_back_sets_the_fault_and_stops_the_agent()
    {
        string before = AuditLines.Connection(TestTimes.Start);
        _data.Audit.Seed("2026-11-22.jsonl", before);
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        _data.Audit.FailNextWrite(bytesKept: 7);

        Assert.Throws<AgentStopException>(() => log.Ended(Guid.NewGuid(), OutpostAgentOutcomeKindEnum.Succeeded, null, 5));

        Assert.Equal(
            ["append audit\\2026-11-22.jsonl", $"cut audit\\2026-11-22.jsonl to {before.Length}"],
            _data.LinesSince(0));
        Assert.Equal(before, _data.Audit.Text("2026-11-22.jsonl"));
        Assert.NotNull(log.Fault);
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public void After_a_failed_write_no_writer_writes_and_each_stops_the_agent(string writer)
    {
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        _data.Audit.FailNextWrite(bytesKept: 0);
        Assert.Throws<AgentStopException>(() => log.Connection(true, null));
        _data.Audit.Heal();
        int count = _data.Changes.Count;

        Assert.Throws<AgentStopException>(() => Write(log, writer));
        Assert.Throws<AgentStopException>(() => log.Started("0.1.0", [], "the start stops"));

        Assert.Empty(_data.LinesSince(count));
    }

    // A log that cannot be trusted still records why the agent stops, in started; nothing else.
    [Theory]
    [MemberData(nameof(Writers))]
    public void With_a_fault_from_reading_only_started_writes_and_every_other_writer_stops_the_agent(string writer)
    {
        _data.Audit.Seed(
            "2026-11-21.jsonl",
            AuditLines.Connection(TestTimes.Start.AddDays(-1)) + "not an entry\n" + AuditLines.Connection(TestTimes.Start.AddDays(-1)));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        Assert.NotNull(log.Fault);

        Assert.Throws<AgentStopException>(() => Write(log, writer));
        Assert.Empty(_data.Changes);

        log.Started("0.1.0", [], log.Fault);

        Assert.Equal(["started"], AppendedSince(_data, 0).Select(Kind));
    }

    // An exception of the cut back is not reported: the torn entry is safe to find at the next start, which cuts it.
    [Fact]
    public void A_failed_cut_back_is_not_reported_and_the_next_open_cuts_the_torn_entry()
    {
        string before = AuditLines.Connection(TestTimes.Start);
        _data.Audit.Seed("2026-11-22.jsonl", before);

        using (AuditLog log = AuditLog.Open(_data.Audit, _clock))
        {
            _data.Audit.FailNextWrite(bytesKept: 7);
            _data.Audit.FailCut("2026-11-22.jsonl");

            Assert.Throws<AgentStopException>(() => log.Connection(false, "unreachable"));
            Assert.NotNull(log.Fault);
        }

        _data.Audit.Heal();
        int count = _data.Changes.Count;

        using AuditLog reopened = AuditLog.Open(_data.Audit, _clock);

        Assert.Null(reopened.Fault);
        Assert.Equal([$"cut audit\\2026-11-22.jsonl to {before.Length}"], _data.LinesSince(count));
    }

    // §13 as S6 drafts it: after a failed write the next start finds the log as it was, or with that entry whole when
    // cutting it back failed too.
    [Fact]
    public void An_entry_whose_write_failed_after_it_was_whole_is_found_at_the_next_open_when_its_cut_back_failed()
    {
        Guid command = Guid.NewGuid();

        using (AuditLog log = AuditLog.Open(_data.Audit, _clock))
        {
            log.Received(TestCommands.Of(command, OutpostModeEnum.Write, TestTimes.Start));
            _data.Audit.FailNextWrite(bytesKept: int.MaxValue);
            _data.Audit.FailCut("2026-11-22.jsonl");

            Assert.Throws<AgentStopException>(() => log.Ended(command, OutpostAgentOutcomeKindEnum.Succeeded, null, 5));
        }

        _data.Audit.Heal();

        using AuditLog reopened = AuditLog.Open(_data.Audit, _clock);

        Assert.Null(reopened.Fault);
        Assert.Equal(OutpostAgentOutcomeKindEnum.Succeeded, reopened.Find(command)!.Kind);
    }

    // The owner's Q16: stopped records the reason of a stop that is already under way, so a failed write of its own is
    // cut back as any entry's and throws nothing.
    [Fact]
    public void A_failed_write_of_stopped_cuts_the_file_back_sets_the_fault_and_throws_nothing()
    {
        string before = AuditLines.Connection(TestTimes.Start);
        _data.Audit.Seed("2026-11-22.jsonl", before);
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        _data.Audit.FailNextWrite(bytesKept: 7);

        log.Stopped("the poll numbers are used up; pair the agent anew");

        Assert.Equal(
            ["append audit\\2026-11-22.jsonl", $"cut audit\\2026-11-22.jsonl to {before.Length}"],
            _data.LinesSince(0));
        Assert.Equal(before, _data.Audit.Text("2026-11-22.jsonl"));
        Assert.NotNull(log.Fault);
    }

    // The owner's Q16: after a failed write of the log, or with a log that cannot be read, stopped writes nothing and
    // throws nothing.
    [Theory]
    [InlineData("failed write")]
    [InlineData("read fault")]
    public void With_a_fault_stopped_writes_nothing_and_throws_nothing(string fault)
    {
        if (fault == "read fault")
        {
            _data.Audit.Seed("2026-11-21.jsonl", "not an entry\n" + AuditLines.Connection(TestTimes.Start.AddDays(-1)));
        }

        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        if (fault == "failed write")
        {
            _data.Audit.FailNextWrite(bytesKept: 0);
            Assert.Throws<AgentStopException>(() => log.Connection(true, null));
            _data.Audit.Heal();
        }

        Assert.NotNull(log.Fault);
        int count = _data.Changes.Count;

        log.Stopped(@"outcomes\9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6.json cannot be written");

        Assert.Empty(_data.LinesSince(count));
    }

    // What the log gives for the received list, for the restart and for the poll numbers follows each write.
    [Fact]
    public void Each_write_updates_what_Find_Unconfirmed_and_HighestReservation_give()
    {
        Guid command = Guid.NewGuid();
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        Assert.Null(log.Find(command));
        Assert.Null(log.HighestReservation(Agent));

        log.Received(TestCommands.Of(command, OutpostModeEnum.Read, TestTimes.Start));
        AuditCommand received = new(command, OutpostModeEnum.Read, true, null, null, null, false);
        Assert.Equal(received, log.Find(command));
        Assert.Equal([received], log.Unconfirmed);

        log.Ended(command, OutpostAgentOutcomeKindEnum.Failed, "script-error", 40);
        AuditCommand ended = received with { Kind = OutpostAgentOutcomeKindEnum.Failed, Code = "script-error", DurationMs = 40 };
        Assert.Equal(ended, log.Find(command));
        Assert.Equal([ended], log.Unconfirmed);

        log.Confirmed(command);
        Assert.Equal(ended with { Confirmed = true }, log.Find(command));
        Assert.Empty(log.Unconfirmed);

        log.Reservation(Agent, 700);
        log.Reservation(Agent, 900);
        Assert.Equal(900, log.HighestReservation(Agent));
    }

    private static void Write(AuditLog log, string writer)
    {
        Guid command = Guid.NewGuid();

        switch (writer)
        {
            case "policy":
                log.Policy(null);
                break;
            case "connection":
                log.Connection(true, null);
                break;
            case "bases":
                log.Bases(["accounting"], []);
                break;
            case "received":
                log.Received(TestCommands.Of(command, OutpostModeEnum.Read, TestTimes.Start));
                break;
            case "refused":
                log.Refused(command, "accounting", OutpostModeEnum.Read, "expired");
                break;
            case "ended":
                log.Ended(command, OutpostAgentOutcomeKindEnum.Succeeded, null, 5);
                break;
            case "confirmed":
                log.Confirmed(command);
                break;
            default:
                log.Reservation(Agent, 900);
                break;
        }
    }
}
