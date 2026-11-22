using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Polls;
using Moltaro.OnecOutpost.Agent.Tests.FakeFiles;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Polls;

// The numbers of the polls of one identity (concept rule 10, §6.2 of docs/protocol.md, the S6a brief), with the fake
// clock, which moves only when a test moves it. A run with no reservation of its identity in its log counts its polls
// 1, 2, 3, … until an ordinary answer; otherwise a number is the greatest of the clock's UTC ticks, one above the last
// number, one above the greatest newest of an answer to an older poll, and, for the first number of a run, the highest
// reservation. Before it gives a number at or above the last bound, or any number while no reservation is known, the
// agent records a reservation of that number plus a day of ticks; a number above 2^62 is recorded so and stops it.
public sealed class PollNumberTests
{
    private const long Span = PollNumbers.ReservationSpan;

    private const long Greatest = OutpostProtocol.MaxPollNumber;

    private static readonly Guid Agent = PairedFolder.Agent;

    private static readonly long Ticks = TestTimes.Start.UtcTicks;

    private static readonly DateTimeOffset Before = TestTimes.Start.AddDays(-1);

    private readonly MemoryDataFolder _data = new();

    private readonly FakeClock _clock = new(TestTimes.Start);

    [Fact]
    public void The_reservation_is_a_day_of_ticks()
    {
        Assert.Equal(TimeSpan.TicksPerDay, PollNumbers.ReservationSpan);
    }

    // A log with no reservation of the identity, as after pairing: the first poll is 1, recorded before it is given.
    [Fact]
    public void A_run_without_a_reservation_of_its_identity_counts_its_polls_from_1_after_one_reservation()
    {
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        PollNumbers numbers = new(log, Agent, _clock);
        Assert.Empty(_data.Changes);

        Assert.Equal(1, numbers.Next());
        Assert.Equal([1 + Span], ReservationsSince(0));

        Assert.Equal(2, numbers.Next());
        Assert.Equal(3, numbers.Next());
        Assert.Equal([1 + Span], ReservationsSince(0));
    }

    // The acceptance of S6: a reservation of another identity, from an earlier pairing, does not count.
    [Fact]
    public void A_reservation_of_another_identity_does_not_count()
    {
        _data.Audit.Seed(FileOf(Before), Reservation(Before, Guid.NewGuid(), 1L << 61));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        PollNumbers numbers = new(log, Agent, _clock);

        Assert.Equal(1, numbers.Next());
    }

    [Fact]
    public void An_ordinary_answer_ends_counting_and_the_next_number_comes_from_the_clock_after_its_reservation()
    {
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        PollNumbers numbers = new(log, Agent, _clock);
        Assert.Equal(1, numbers.Next());

        numbers.Answered(null);

        Assert.Equal(Ticks, numbers.Next());
        Assert.Equal([1 + Span, Ticks + Span], ReservationsSince(0));
    }

    // One poll is open at a time and the last number given was a counted one, so a server that answers it as older has
    // recorded polls that the log does not hold: the log was cleared, and with it the commands it had received.
    [Fact]
    public void An_answer_to_an_older_poll_while_counting_records_a_reservation_that_leaves_no_number_and_stops_the_agent()
    {
        using (AuditLog log = AuditLog.Open(_data.Audit, _clock))
        {
            PollNumbers numbers = new(log, Agent, _clock);
            Assert.Equal(1, numbers.Next());

            Assert.Throws<AgentStopException>(() => numbers.Answered(5));

            Assert.Equal([1 + Span, Greatest + 1], ReservationsSince(0));
        }

        using AuditLog reopened = AuditLog.Open(_data.Audit, _clock);

        Assert.Throws<AgentStopException>(() => new PollNumbers(reopened, Agent, _clock));
    }

    // Every start reserves anew, at or above the highest bound of its identity, whatever the clock says.
    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    public void The_first_number_of_a_run_is_at_least_the_highest_reservation_and_is_reserved_before_it_is_given(int daysAhead)
    {
        long reserved = Ticks + (daysAhead * Span);
        _data.Audit.Seed(FileOf(Before), Reservation(Before, Agent, reserved));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        PollNumbers numbers = new(log, Agent, _clock);
        long first = Math.Max(Ticks, reserved);

        Assert.Equal(first, numbers.Next());
        Assert.Equal([first + Span], ReservationsSince(0));

        Assert.Equal(first + 1, numbers.Next());
        Assert.Equal([first + Span], ReservationsSince(0));
    }

    [Fact]
    public void A_reservation_is_recorded_before_a_number_at_the_last_bound_and_not_before_one_below_it()
    {
        _data.Audit.Seed(FileOf(Before), Reservation(Before, Agent, 1000));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        PollNumbers numbers = new(log, Agent, _clock);
        Assert.Equal(Ticks, numbers.Next());
        int count = _data.Changes.Count;

        _clock.Advance(TimeSpan.FromTicks(Span - 1));
        Assert.Equal(Ticks + Span - 1, numbers.Next());
        Assert.Empty(ReservationsSince(count));

        _clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(Ticks + Span, numbers.Next());
        Assert.Equal([Ticks + Span + Span], ReservationsSince(count));
    }

    // Rule 10: after an answer to an older poll, the agent continues above the newest number that it names, so that a
    // clock set back stalls nothing.
    [Fact]
    public void A_number_is_above_the_last_number_and_above_the_newest_of_an_answer_to_an_older_poll()
    {
        _data.Audit.Seed(FileOf(Before), Reservation(Before, Agent, 1000));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        PollNumbers numbers = new(log, Agent, _clock);
        Assert.Equal(Ticks, numbers.Next());
        int count = _data.Changes.Count;

        numbers.Answered(Ticks + 5000);
        Assert.Equal(Ticks + 5001, numbers.Next());

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(Ticks + TimeSpan.TicksPerMillisecond, numbers.Next());
        Assert.Empty(ReservationsSince(count));

        numbers.Answered(Ticks + Span + 7);
        Assert.Equal(Ticks + Span + 8, numbers.Next());
        Assert.Equal([Ticks + Span + 8 + Span], ReservationsSince(count));
    }

    // The acceptance of S6: an answer to an older poll whose newest is 2^62 stops the agent after it records the
    // reservation of the next number, and every later start stops too.
    [Fact]
    public void An_answer_whose_newest_is_the_greatest_number_stops_the_agent_after_the_reservation_of_the_next_number()
    {
        _data.Audit.Seed(FileOf(Before), Reservation(Before, Agent, 1000));

        using (AuditLog log = AuditLog.Open(_data.Audit, _clock))
        {
            PollNumbers numbers = new(log, Agent, _clock);
            Assert.Equal(Ticks, numbers.Next());
            int count = _data.Changes.Count;

            numbers.Answered(Greatest);

            Assert.Throws<AgentStopException>(() => numbers.Next());
            Assert.Equal([Greatest + 1 + Span], ReservationsSince(count));
        }

        using AuditLog reopened = AuditLog.Open(_data.Audit, _clock);

        Assert.Throws<AgentStopException>(() => new PollNumbers(reopened, Agent, _clock));
    }

    // From S6a's code review, round 1: a bound reserved within a day of ticks below 2^62 lies above 2^62, and still no
    // number above 2^62 is given (§6.2: a number is from 1 to 2^62). The one that would pass it records its reservation,
    // the record of this first stop, and stops the agent. Here the bound is found at a start.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_reservation_near_the_greatest_number_found_at_a_start_gives_no_number_above_it()
    {
        _data.Audit.Seed(FileOf(Before), Reservation(Before, Agent, Greatest - 2));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        PollNumbers numbers = new(log, Agent, _clock);

        Assert.Equal(Greatest - 2, numbers.Next());
        Assert.Equal(Greatest - 1, numbers.Next());
        Assert.Equal(Greatest, numbers.Next());
        int count = _data.Changes.Count;

        Assert.Throws<AgentStopException>(() => numbers.Next());
        Assert.Equal([Greatest + 1 + Span], ReservationsSince(count));
    }

    // The same, with the bound recorded after an answer to an older poll whose newest is 2^62 - 1, and a second such
    // answer whose newest is 2^62.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void After_answers_whose_newest_comes_near_the_greatest_number_no_number_above_it_is_given()
    {
        _data.Audit.Seed(FileOf(Before), Reservation(Before, Agent, 1000));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        PollNumbers numbers = new(log, Agent, _clock);
        Assert.Equal(Ticks, numbers.Next());

        numbers.Answered(Greatest - 1);
        Assert.Equal(Greatest, numbers.Next());
        int count = _data.Changes.Count;

        numbers.Answered(Greatest);

        Assert.Throws<AgentStopException>(() => numbers.Next());
        Assert.Equal([Greatest + 1 + Span], ReservationsSince(count));
    }

    [Fact]
    public void A_highest_reservation_above_the_greatest_number_stops_the_agent_before_any_poll()
    {
        _data.Audit.Seed(FileOf(Before), Reservation(Before, Agent, Greatest + 1));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        AgentStopException exception = Assert.Throws<AgentStopException>(() => new PollNumbers(log, Agent, _clock));

        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.Empty(_data.Changes);
    }

    // The bounds of the reservations of the identity that the changes since `count` recorded, in their order.
    private List<long> ReservationsSince(int count)
    {
        return
        [
            .. AuditEntries.AppendedSince(_data, count)
                .Where(entry => AuditEntries.Kind(entry) == "reservation")
                .Select(entry =>
                {
                    Assert.Equal(Agent.ToString(), entry.GetProperty("agent").GetString());

                    return entry.GetProperty("below").GetInt64();
                }),
        ];
    }
}
