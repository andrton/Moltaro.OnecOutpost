using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Tests.FakeFiles;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Audit;

// The retention of the audit log, with the fake clock ("Carried over from S2", the S6a brief): RemoveExpired holds the
// whole rule. A file goes once its last entry is more than auditRetentionDays plus a day older than the agent's clock,
// and a file that holds no entry goes too, in the order of their names; a file that holds an entry of a command whose
// outcome is not confirmed, and the file that holds the highest reservation of the identity, stay. Nothing goes when
// the clock is more than a day away from the time of the verified answer, so a clock moved ahead wipes nothing out.
// That nothing goes before the first verified answer of a run is the call site's, in S6b.
public sealed class RetentionTests
{
    private const int RetentionDays = 1;

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly MemoryDataFolder _data = new();

    private readonly FakeClock _clock = new(Now);

    // With one day of retention, a file goes once its last entry is more than two days old: before 2026-11-20 at noon.
    [Fact(Skip = SkipReason.NotImplemented)]
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
    [Fact(Skip = SkipReason.NotImplemented)]
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
    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_file_with_an_entry_of_an_unconfirmed_command_and_the_file_of_the_highest_reservation_are_kept_past_the_retention()
    {
        Guid unconfirmed = Guid.NewGuid();
        Guid refused = Guid.NewGuid();
        Guid confirmed = Guid.NewGuid();
        Guid confirmedLater = Guid.NewGuid();
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
        _data.Audit.Seed(
            "2026-10-09.jsonl",
            Received(october.AddDays(8), confirmedLater, OutpostModeEnum.Read)
                + Ended(october.AddDays(8), confirmedLater, "succeeded", null, 4));
        _data.Audit.Seed("2026-11-22.jsonl", Confirmed(Now.AddHours(-1), confirmedLater));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        int count = _data.Changes.Count;

        log.RemoveExpired(PairedFolder.Agent, Now, RetentionDays);

        Assert.Equal(
            [
                "delete audit\\2026-10-02.jsonl",
                "delete audit\\2026-10-04.jsonl",
                "delete audit\\2026-10-05.jsonl",
                "delete audit\\2026-10-06.jsonl",
                "delete audit\\2026-10-09.jsonl",
            ],
            _data.LinesSince(count));
        Assert.Null(log.Find(confirmed));
        Assert.NotNull(log.Find(confirmedLater));
        Assert.True(log.Find(unconfirmed)!.Received);
        Assert.NotNull(log.Find(refused));
        Assert.Equal(900, log.HighestReservation(PairedFolder.Agent));
    }

    // A day either way is the tolerance of the agent's clock against the server's.
    [Theory(Skip = SkipReason.NotImplemented)]
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

    [Fact(Skip = SkipReason.NotImplemented)]
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
}
