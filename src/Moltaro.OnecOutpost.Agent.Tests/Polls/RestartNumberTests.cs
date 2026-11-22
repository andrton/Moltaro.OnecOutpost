using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Polls;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Polls;

// Poll numbers grow across a restart, with the fake clock (the acceptance of S6, §6.2 and §13 of docs/protocol.md):
// each start of the agent is AgentStart.Open on the same data folder with a clock of its own, which may be set back,
// and the first poll of every start records a reservation. A hop that kept a poll from a time the clock was ahead can
// never replay it as the newest after a restart.
public sealed class RestartNumberTests
{
    private readonly PairedFolder _folder = PairedFolder.Create();

    [Fact(Skip = SkipReason.NotImplemented)]
    public void After_a_restart_with_the_clock_set_behind_the_last_number_every_poll_is_above_every_poll_sent_before()
    {
        List<long> sent = Run(TestTimes.Start, 3);

        List<long> after = Run(TestTimes.Start.AddHours(-1), 3);

        Assert.All(after, number => Assert.True(number > sent.Max()));
    }

    // Whatever the names and the times of the audit files: the clock puts the entries of each period in the files of
    // its days, ten days ahead, then three, then today.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void After_two_periods_with_the_clock_ahead_by_less_each_time_every_poll_after_the_last_restart_is_above_every_poll_sent_before()
    {
        List<long> tenDaysAhead = Run(TestTimes.Start.AddDays(10), 3);
        List<long> threeDaysAhead = Run(TestTimes.Start.AddDays(3), 3);

        List<long> after = Run(TestTimes.Start, 3);

        long highestBefore = tenDaysAhead.Concat(threeDaysAhead).Max();
        Assert.All(after, number => Assert.True(number > highestBefore));
        Assert.True(_folder.Data.Audit.Has("2026-12-02.jsonl"));
        Assert.True(_folder.Data.Audit.Has("2026-11-25.jsonl"));
    }

    // A reservation is recorded and on the disk before a poll at or above the last bound, and at every start.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_first_poll_of_every_start_records_a_reservation_before_its_number_is_given()
    {
        foreach (DateTimeOffset start in new[] { TestTimes.Start, TestTimes.Start.AddDays(3), TestTimes.Start.AddHours(-1) })
        {
            AgentRun run = _folder.Start(new FakeClock(start));
            int count = _folder.Data.Changes.Count;

            long number = run.Numbers.Next();

            JsonElement reservation = AuditEntries.AppendedSince(_folder.Data, count).Single();
            Assert.Equal("reservation", AuditEntries.Kind(reservation));
            Assert.Equal(number + PollNumbers.ReservationSpan, reservation.GetProperty("below").GetInt64());

            run.Numbers.Answered(null);
            run.Log.Dispose();
        }
    }

    // The acceptance of S6: after the numbers ran out through the newest of an answer, every later start records started
    // with an error.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void After_an_answer_whose_newest_is_the_greatest_number_every_later_start_records_started_with_an_error()
    {
        AgentRun run = _folder.Start(new FakeClock(TestTimes.Start));
        run.Numbers.Next();
        run.Numbers.Answered(null);
        run.Numbers.Next();
        run.Numbers.Answered(OutpostProtocol.MaxPollNumber);
        Assert.Throws<AgentStopException>(() => run.Numbers.Next());
        run.Log.Dispose();

        foreach (int restart in new[] { 1, 2 })
        {
            int count = _folder.Data.Changes.Count;

            AgentStopException exception = Assert.Throws<AgentStopException>(
                () => _folder.Start(new FakeClock(TestTimes.Start.AddMinutes(restart))));

            JsonElement started = AuditEntries.AppendedSince(_folder.Data, count).Last();
            Assert.Equal("started", AuditEntries.Kind(started));
            Assert.Equal(exception.Message, started.GetProperty("error").GetString());
        }
    }

    // One start of the agent: `polls` numbers, the first after an ordinary answer to end counting when the log holds
    // no reservation yet, and the log released at the end.
    private List<long> Run(DateTimeOffset start, int polls)
    {
        AgentRun run = _folder.Start(new FakeClock(start));
        List<long> numbers = [];

        for (int poll = 0; poll < polls; poll++)
        {
            numbers.Add(run.Numbers.Next());
            run.Numbers.Answered(null);
        }

        run.Log.Dispose();

        return numbers;
    }
}
