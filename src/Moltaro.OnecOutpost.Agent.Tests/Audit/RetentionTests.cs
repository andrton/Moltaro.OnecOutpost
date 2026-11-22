using System.Globalization;
using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Agent.Records;
using Moltaro.OnecOutpost.Agent.Tests.FakeFiles;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Audit;

// The retention of the audit log, with the fake clock ("Carried over from S2", the S6a brief): RemoveExpired holds the
// whole rule. A file goes once its last entry is more than auditRetentionDays plus a day older than the agent's clock,
// and a file that holds no entry goes too, in the order of their names; a file that holds an entry of a command whose
// outcome is not confirmed, and the file that holds the highest reservation of the identity, stay. The files that hold
// the entries of one command go together, the file of its confirmed last, so that no command is left with its received
// or ended and without its confirmed. Nothing goes when the clock is more than a day away from the time of the verified
// answer, so a clock moved ahead wipes nothing out.
// That nothing goes before the first verified answer of a run is the call site's, in S6b.
public sealed class RetentionTests
{
    private const int RetentionDays = 1;

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly MemoryDataFolder _data = new();

    private readonly FakeClock _clock = new(Now);

    // With one day of retention, a file goes once its last entry is more than two days old: before 2026-11-20 at noon.
    [Fact]
    public void Files_older_than_the_retention_plus_a_day_by_their_last_entry_and_files_with_no_entry_are_removed_in_the_order_of_their_names()
    {
        _data.Audit.Seed("2026-11-21.jsonl", Connection(new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero)));
        _data.Audit.Seed(
            "2026-11-20.jsonl",
            Connection(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero))
                + Connection(new DateTimeOffset(2026, 11, 20, 11, 59, 59, 999, TimeSpan.Zero)));
        _data.Audit.Seed("2026-11-18.jsonl", Connection(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero)));
        _data.Audit.Seed("2026-11-17.jsonl", "");
        _data.Audit.Seed("notes.txt", "");
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        int count = _data.Changes.Count;

        log.RemoveExpired(PairedFolder.Agent, Now, RetentionDays);

        Assert.Equal(
            ["delete audit\\2026-11-17.jsonl", "delete audit\\2026-11-18.jsonl", "delete audit\\2026-11-20.jsonl"],
            _data.LinesSince(count));
        Assert.True(_data.Audit.Has("2026-11-21.jsonl"));
        Assert.True(_data.Audit.Has("notes.txt"));
        Assert.True(_data.Audit.Has("lock"));
    }

    // A file's age counts from its last entry, and a file exactly as old as the limit is not older than it.
    [Fact]
    public void A_file_whose_last_entry_is_exactly_the_retention_plus_a_day_old_is_kept_whatever_its_earlier_entries()
    {
        _data.Audit.Seed(
            "2026-11-20.jsonl",
            Connection(new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero))
                + Connection(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero)));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        int count = _data.Changes.Count;

        log.RemoveExpired(PairedFolder.Agent, Now, RetentionDays);

        Assert.Empty(_data.LinesSince(count));
    }

    // The log is the journal of rules 3, 10 and 11: a command not confirmed is still listed in every poll, and the
    // highest reservation bounds every poll after a restart. A command whose entries are all removed is forgotten.
    [Fact]
    public void A_file_with_an_entry_of_an_unconfirmed_command_and_the_file_of_the_highest_reservation_are_kept_past_the_retention()
    {
        Guid unconfirmed = Guid.NewGuid();
        Guid refused = Guid.NewGuid();
        Guid confirmed = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        DateTimeOffset october = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        _data.Audit.Seed("2026-10-01.jsonl", Received(october, unconfirmed, OutpostModeEnum.Write));
        _data.Audit.Seed("2026-10-02.jsonl", Reservation(october.AddDays(1), PairedFolder.Agent, 500));
        _data.Audit.Seed("2026-10-03.jsonl", Reservation(october.AddDays(2), PairedFolder.Agent, 900));
        _data.Audit.Seed("2026-10-04.jsonl", Reservation(october.AddDays(3), other, 5000));
        _data.Audit.Seed(
            "2026-10-05.jsonl",
            Received(october.AddDays(4), confirmed, OutpostModeEnum.Read)
                + Ended(october.AddDays(4), confirmed, "succeeded", null, 3));
        _data.Audit.Seed("2026-10-06.jsonl", Confirmed(october.AddDays(5), confirmed));
        _data.Audit.Seed("2026-10-07.jsonl", Ended(october.AddDays(6), unconfirmed, "unknown", "agent-stopped", null));
        _data.Audit.Seed("2026-10-08.jsonl", Refused(october.AddDays(7), refused, OutpostModeEnum.Write, "expired"));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        int count = _data.Changes.Count;

        log.RemoveExpired(PairedFolder.Agent, Now, RetentionDays);

        Assert.Equal(
            [
                "delete audit\\2026-10-02.jsonl",
                "delete audit\\2026-10-04.jsonl",
                "delete audit\\2026-10-05.jsonl",
                "delete audit\\2026-10-06.jsonl",
            ],
            _data.LinesSince(count));
        Assert.Null(log.Find(confirmed));
        Assert.True(log.Find(unconfirmed)!.Received);
        Assert.NotNull(log.Find(refused));
        Assert.Equal(900, log.HighestReservation(PairedFolder.Agent));
    }

    // A day either way is the tolerance of the agent's clock against the server's.
    [Theory]
    [InlineData(86_400_001, false)]
    [InlineData(-86_400_001, false)]
    [InlineData(86_400_000, true)]
    [InlineData(-86_400_000, true)]
    [InlineData(0, true)]
    public void Nothing_is_removed_when_the_clock_is_more_than_a_day_away_from_the_time_of_the_answer(
        long answerOffsetMs,
        bool removes)
    {
        _data.Audit.Seed("2026-11-01.jsonl", Connection(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero)));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        int count = _data.Changes.Count;

        log.RemoveExpired(PairedFolder.Agent, Now + TimeSpan.FromMilliseconds(answerOffsetMs), RetentionDays);

        string[] expected = removes ? ["delete audit\\2026-11-01.jsonl"] : [];
        Assert.Equal(expected, _data.LinesSince(count));
    }

    [Fact]
    public void A_file_that_cannot_be_deleted_stays_until_a_later_call_and_nothing_is_recorded()
    {
        _data.Audit.Seed("2026-11-01.jsonl", Connection(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero)));
        _data.Audit.Seed("2026-11-02.jsonl", Connection(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero)));
        _data.Audit.FailDelete("2026-11-01.jsonl");
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        int count = _data.Changes.Count;

        log.RemoveExpired(PairedFolder.Agent, Now, RetentionDays);

        Assert.True(_data.Audit.Has("2026-11-01.jsonl"));
        Assert.False(_data.Audit.Has("2026-11-02.jsonl"));

        _data.Audit.Heal();
        log.RemoveExpired(PairedFolder.Agent, Now, RetentionDays);

        Assert.False(_data.Audit.Has("2026-11-01.jsonl"));
        Assert.DoesNotContain(_data.LinesSince(count), line => line.StartsWith("append ", StringComparison.Ordinal));
    }

    // The files that hold the entries of one command go together (the brief, from S6a's code review, round 1). Command C
    // was received and ended in a file that stays, and confirmed in a later file past the retention; command D was
    // received there and confirmed in a third. Had the later files gone, every poll would list C and D again, and every
    // later start would stop on an ended without its file.
    [Theory]
    [InlineData("an unconfirmed command")]
    [InlineData("the highest reservation")]
    public void A_file_past_the_retention_stays_while_a_file_that_stays_holds_an_entry_of_one_of_its_commands(string keeper)
    {
        Guid unconfirmed = Guid.NewGuid();
        Guid c = Guid.NewGuid();
        Guid d = Guid.NewGuid();
        string keeping = keeper == "an unconfirmed command"
            ? Received(TimeIn("2026-10-01.jsonl"), unconfirmed, OutpostModeEnum.Write)
            : Reservation(TimeIn("2026-10-01.jsonl"), PairedFolder.Agent, 900);
        _data.Audit.Seed(
            "2026-10-01.jsonl",
            keeping
                + Received(TimeIn("2026-10-01.jsonl"), c, OutpostModeEnum.Read)
                + Ended(TimeIn("2026-10-01.jsonl"), c, "succeeded", null, 3));
        _data.Audit.Seed(
            "2026-10-02.jsonl",
            Confirmed(TimeIn("2026-10-02.jsonl"), c)
                + Received(TimeIn("2026-10-02.jsonl"), d, OutpostModeEnum.Read)
                + Ended(TimeIn("2026-10-02.jsonl"), d, "succeeded", null, 4));
        _data.Audit.Seed("2026-10-03.jsonl", Confirmed(TimeIn("2026-10-03.jsonl"), d));

        using (AuditLog log = AuditLog.Open(_data.Audit, _clock))
        {
            int count = _data.Changes.Count;

            log.RemoveExpired(PairedFolder.Agent, Now, RetentionDays);

            Assert.Empty(_data.LinesSince(count));
            Assert.True(log.Find(c)!.Confirmed);
            Assert.True(log.Find(d)!.Confirmed);

            Guid[] expected = keeper == "an unconfirmed command" ? [unconfirmed] : [];
            Assert.Equal(expected, log.Unconfirmed.Select(command => command.Id));
        }

        AssertARestartGoesOn();
    }

    // A file that cannot be deleted keeps every file not yet deleted that shares a command with it, and the file of a
    // confirmed goes after the other files of its command, also when the clock was set back and put the confirmed in a
    // file named before its received. In both cases the file of the received cannot be deleted at first. Had the file of
    // the confirmed gone, every poll would list the command again, and every later start would stop on an ended without
    // its file. A file that shares no command with them goes meanwhile.
    [Theory]
    [InlineData("2026-10-01.jsonl", "2026-10-02.jsonl")]
    [InlineData("2026-10-02.jsonl", "2026-10-01.jsonl")]
    public void A_file_that_cannot_be_deleted_keeps_the_files_that_share_a_command_with_it_and_a_confirmed_goes_last(
        string receivedFile,
        string confirmedFile)
    {
        Guid command = Guid.NewGuid();
        _data.Audit.Seed(
            receivedFile,
            Received(TimeIn(receivedFile), command, OutpostModeEnum.Write)
                + Ended(TimeIn(receivedFile), command, "succeeded", null, 5));
        _data.Audit.Seed(confirmedFile, Confirmed(TimeIn(confirmedFile), command));
        _data.Audit.Seed("2026-10-03.jsonl", Connection(TimeIn("2026-10-03.jsonl")));
        _data.Audit.FailDelete(receivedFile);

        using (AuditLog log = AuditLog.Open(_data.Audit, _clock))
        {
            int count = _data.Changes.Count;

            log.RemoveExpired(PairedFolder.Agent, Now, RetentionDays);

            Assert.Equal(["delete audit\\2026-10-03.jsonl"], _data.LinesSince(count));
            Assert.True(log.Find(command)!.Confirmed);
            Assert.Empty(log.Unconfirmed);
        }

        AssertARestartGoesOn();
        _data.Audit.Heal();

        using (AuditLog log = AuditLog.Open(_data.Audit, _clock))
        {
            int count = _data.Changes.Count;

            log.RemoveExpired(PairedFolder.Agent, Now, RetentionDays);

            Assert.Equal([$"delete audit\\{receivedFile}", $"delete audit\\{confirmedFile}"], _data.LinesSince(count));
            Assert.Null(log.Find(command));
        }
    }

    // Nine in the morning of the UTC day that names an audit file, so that its entries are where the clock put them.
    private static DateTimeOffset TimeIn(string file)
    {
        DateTime day = DateTime.ParseExact(file[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new DateTimeOffset(day.AddHours(9), TimeSpan.Zero);
    }

    // A restart finds nothing that stops it: every command that ended either is confirmed or has its outbox file, here
    // an empty outbox.
    private void AssertARestartGoesOn()
    {
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        CommandRecords.Open(log, Outbox.Open(_data.Outcomes));
    }
}
