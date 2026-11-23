using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Audit;

// What the audit log knows of its commands and reservations after a restart, the agent's journal of rules 3, 10 and 11
// (the S6a brief): Find gives the facts of a command that a received, a refused with a command, an ended or a confirmed
// names; Unconfirmed gives the received list of a poll (§6.2); HighestReservation the greatest bound of an identity.
// Only the last ended or refused depends on the order of the files, which the clock can upset; every other fact is a
// set or a maximum, whatever the files it is in.
public sealed class AuditFindTests
{
    private static readonly DateTimeOffset Day20 = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Day22 = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly MemoryDataFolder _data = new();

    private readonly FakeClock _clock = new(TestTimes.Start);

    [Fact]
    public void Find_gives_the_facts_of_a_command_that_any_entry_names_and_null_for_any_other()
    {
        Guid write = Guid.NewGuid();
        Guid refused = Guid.NewGuid();
        Guid endedOnly = Guid.NewGuid();
        Guid confirmedOnly = Guid.NewGuid();
        _data.Audit.Seed(
            "2026-11-22.jsonl",
            Received(Day22, write, OutpostModeEnum.Write)
                + Ended(Day22, write, "succeeded", null, 1234)
                + Refused(Day22, refused, OutpostModeEnum.Read, "expired")
                + Refused(Day22, null, null, "signature")
                + Ended(Day22, endedOnly, "unknown", "agent-stopped", null)
                + Confirmed(Day22, confirmedOnly)
                + Connection(Day22));

        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        Assert.Equal(
            new AuditCommand(write, OutpostModeEnum.Write, true, OutpostAgentOutcomeKindEnum.Succeeded, null, 1234, false),
            log.Find(write));
        Assert.Equal(
            new AuditCommand(refused, OutpostModeEnum.Read, false, OutpostAgentOutcomeKindEnum.Refused, "expired", null, false),
            log.Find(refused));
        Assert.Equal(
            new AuditCommand(endedOnly, null, false, OutpostAgentOutcomeKindEnum.Unknown, "agent-stopped", null, false),
            log.Find(endedOnly));
        Assert.Equal(new AuditCommand(confirmedOnly, null, false, null, null, null, true), log.Find(confirmedOnly));
        Assert.Null(log.Find(Guid.NewGuid()));
    }

    // A second ended replaces the first, as after a refusal for size. With the clock set back, it can stand in a file
    // named before its received; the order of the names still decides, and the start reconciles the outbox file.
    [Fact]
    public void The_last_ended_in_the_order_of_file_names_and_lines_gives_the_kind_the_code_and_the_duration()
    {
        Guid inOneFile = Guid.NewGuid();
        Guid acrossFiles = Guid.NewGuid();
        _data.Audit.Seed(
            "2026-11-22.jsonl",
            Received(Day22, inOneFile, OutpostModeEnum.Write)
                + Ended(Day22, inOneFile, "succeeded", null, 1234)
                + Ended(Day22, inOneFile, "unknown", "result-too-large", 1234)
                + Received(Day22, acrossFiles, OutpostModeEnum.Read)
                + Ended(Day22, acrossFiles, "succeeded", null, 77));
        _data.Audit.Seed("2026-11-20.jsonl", Ended(Day20, acrossFiles, "failed", "result-too-large", 77));

        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        Assert.Equal(
            new AuditCommand(inOneFile, OutpostModeEnum.Write, true, OutpostAgentOutcomeKindEnum.Unknown, "result-too-large", 1234, false),
            log.Find(inOneFile));
        Assert.Equal(
            new AuditCommand(acrossFiles, OutpostModeEnum.Read, true, OutpostAgentOutcomeKindEnum.Succeeded, null, 77, false),
            log.Find(acrossFiles));
    }

    // The same order holds for an entry the log writes: after the clock was set back, a second ended goes to a file
    // named before that of the first, and Find gives what a restart reads.
    [Fact]
    public void An_ended_written_to_a_file_named_before_that_of_the_last_ended_does_not_replace_it()
    {
        Guid command = Guid.NewGuid();
        DateTimeOffset ahead = Day22.AddDays(3);
        _data.Audit.Seed(
            "2026-11-25.jsonl",
            Received(ahead, command, OutpostModeEnum.Write) + Ended(ahead, command, "succeeded", null, 1234));
        AuditCommand expected = new(command, OutpostModeEnum.Write, true, OutpostAgentOutcomeKindEnum.Succeeded, null, 1234, false);

        using (AuditLog log = AuditLog.Open(_data.Audit, _clock))
        {
            log.Ended(command, OutpostAgentOutcomeKindEnum.Unknown, "result-too-large", 1234);

            Assert.Equal(expected, log.Find(command));
        }

        using AuditLog reopened = AuditLog.Open(_data.Audit, _clock);

        Assert.Equal(expected, reopened.Find(command));
    }

    [Fact]
    public void Received_and_confirmed_count_whatever_the_file_they_are_in()
    {
        Guid command = Guid.NewGuid();
        _data.Audit.Seed("2026-11-20.jsonl", Confirmed(Day20, command));
        _data.Audit.Seed(
            "2026-11-22.jsonl",
            Received(Day22, command, OutpostModeEnum.Write) + Ended(Day22, command, "succeeded", null, 5));

        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        Assert.Equal(
            new AuditCommand(command, OutpostModeEnum.Write, true, OutpostAgentOutcomeKindEnum.Succeeded, null, 5, true),
            log.Find(command));
        Assert.Empty(log.Unconfirmed);
    }

    // The received list of a poll holds every command received whose outcome the server has not confirmed, refused ones
    // included (§6.2).
    [Fact]
    public void Unconfirmed_gives_every_command_that_an_entry_names_and_no_confirmed_does()
    {
        Guid running = Guid.NewGuid();
        Guid ended = Guid.NewGuid();
        Guid refused = Guid.NewGuid();
        Guid confirmed = Guid.NewGuid();
        _data.Audit.Seed(
            "2026-11-22.jsonl",
            Received(Day22, running, OutpostModeEnum.Write)
                + Received(Day22, ended, OutpostModeEnum.Read)
                + Ended(Day22, ended, "failed", "script-error", 9)
                + Refused(Day22, refused, OutpostModeEnum.Write, "clock-skew")
                + Refused(Day22, null, null, "form")
                + Received(Day22, confirmed, OutpostModeEnum.Read)
                + Ended(Day22, confirmed, "succeeded", null, 3)
                + Confirmed(Day22, confirmed));

        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        Assert.Equal(
            new[] { running, ended, refused }.Order(),
            log.Unconfirmed.Select(command => command.Id).Order());
        Assert.All(log.Unconfirmed, command => Assert.Equal(log.Find(command.Id), command));
    }

    // Reservations only grow, so the highest is also the last, in whichever file the clock put it (§6.2).
    [Fact]
    public void The_highest_reservation_is_the_greatest_bound_of_the_identity_and_null_when_there_is_none()
    {
        Guid other = Guid.NewGuid();
        _data.Audit.Seed("2026-11-20.jsonl", Reservation(Day20, PairedFolder.Agent, 900));
        _data.Audit.Seed(
            "2026-11-22.jsonl",
            Reservation(Day22, PairedFolder.Agent, 500) + Reservation(Day22, other, 5000));

        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        Assert.Equal(900, log.HighestReservation(PairedFolder.Agent));
        Assert.Equal(5000, log.HighestReservation(other));
        Assert.Null(log.HighestReservation(Guid.NewGuid()));
    }
}
