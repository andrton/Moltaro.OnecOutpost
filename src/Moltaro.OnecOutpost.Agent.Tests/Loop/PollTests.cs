using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Polls;
using Moltaro.OnecOutpost.Agent.Tests.FakeHttp;
using Moltaro.OnecOutpost.Agent.Tests.Host;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Loop;

// What a poll of the loop carries and when it opens (§6.2 of docs/protocol.md, concept rules 8 and 10, the S6 brief,
// "AgentLoop.RunAsync"): Next, the agent's time and version, the poll timeout it last learnt, the policy's bases with
// their free workers and run limits, and every command not confirmed as received; the sum of free never passes the
// room left in received. After an answer to an older poll the agent waits a tenth of retryDelayMax and goes on above
// its newest; a newest of 2⁶² leaves no number, and the agent stops for good.
public sealed class PollTests
{
    private static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(10);

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_first_poll_carries_number_1_the_agents_time_and_version_the_poll_timeout_of_agent_json_and_the_bases_of_the_policy()
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        OutpostPollRequest poll = (await host.NextPollAsync()).ReadPoll();

        Assert.Equal(1, poll.Number);
        Assert.Equal(TestTimes.Start, poll.Time);
        Assert.Equal(PairedFolder.Version, poll.AgentVersion);
        Assert.Equal(FakeServer.PollTimeout, poll.PollTimeout);
        Assert.Equal<OutpostPollBase>(
            [
                new OutpostPollBase("accounting", new OutpostPollMode(1, RunLimit), new OutpostPollMode(1, RunLimit)),
                new OutpostPollBase("warehouse", new OutpostPollMode(1, RunLimit), null),
            ],
            poll.Bases);
        Assert.Empty(poll.Received);
        Operations.AssertInOrder(Operations.Since(host.Data, 0), "started", "policy", "reservation", "request poll");
    }

    // The answer teaches a poll timeout of 40 seconds and a lease grace of 15: the next poll carries 40 and waits 55
    // for its answer to begin; a lost answer teaches nothing.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Every_poll_carries_the_poll_timeout_the_agent_last_learnt_and_waits_for_it_plus_the_last_lease_grace()
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        TimeSpan pollTimeout = TimeSpan.FromSeconds(40);
        TimeSpan leaseGrace = TimeSpan.FromSeconds(15);

        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary() with { PollTimeout = pollTimeout, LeaseGrace = leaseGrace });
        ServerExchange second = await host.NextPollAsync();
        Assert.Equal(pollTimeout, second.ReadPoll().PollTimeout);

        await host.PassExactlyAsync(pollTimeout + leaseGrace);
        await host.PassAsync(TimeSpan.FromSeconds(3));
        ServerExchange third = await host.NextPollAsync();

        Assert.Equal(pollTimeout, third.ReadPoll().PollTimeout);
        JsonElement lost = AuditEntries.AppendedSince(host.Data, 0).Last(entry => AuditEntries.Kind(entry) == "connection");
        Assert.Equal("lost", lost.GetProperty("state").GetString());
        Assert.Equal("unreachable", lost.GetProperty("reason").GetString());
    }

    // 9 999 refused commands of an earlier run wait for their confirmation, so the list has room for one more: the
    // first mode keeps its free worker, and the others declare none.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_sum_of_free_never_passes_the_room_left_in_the_list_of_received_commands()
    {
        PairedFolder folder = PairedFolder.Create();
        List<Guid> refused = [];
        System.Text.StringBuilder lines = new();

        for (int index = 0; index < OutpostProtocol.MaxReceived - 1; index++)
        {
            Guid id = Guid.NewGuid();
            refused.Add(id);
            lines.Append(Refused(EarlierRun.Time, id, OutpostModeEnum.Read, OutpostOutcomeCodes.NotServed));
            folder.Data.Outcomes.Seed($"{id}.json", TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Refused, OutpostOutcomeCodes.NotServed));
        }

        EarlierRun.Append(folder, lines.ToString());
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        OutpostPollRequest poll = (await host.NextPollAsync()).ReadPoll();

        Assert.Equal(refused.Order(), poll.Received.Order());
        Assert.Equal<OutpostPollBase>(
            [
                new OutpostPollBase("accounting", new OutpostPollMode(1, RunLimit), new OutpostPollMode(0, RunLimit)),
                new OutpostPollBase("warehouse", new OutpostPollMode(0, RunLimit), null),
            ],
            poll.Bases);
    }

    // The log holds a reservation of the identity, so the agent numbers from its clock (§6.2). The server's newest is
    // ten days of ticks above the first poll.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task After_an_answer_to_an_older_poll_the_agent_waits_a_tenth_of_retryDelayMax_and_numbers_its_polls_above_the_newest()
    {
        PairedFolder folder = PairedFolder.Create();
        EarlierRun.Append(folder, Reservation(EarlierRun.Time, PairedFolder.Agent, TestTimes.Start.UtcTicks));
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        ServerExchange first = await host.NextPollAsync();
        long newest = first.ReadPoll().Number + (10 * PollNumbers.ReservationSpan);
        first.AnswerPoll(host.Server.Older(newest));
        await host.PassExactlyAsync(TimeSpan.FromSeconds(3));
        ServerExchange second = await host.NextPollAsync();

        Assert.Equal(newest + 1, second.ReadPoll().Number);
        JsonElement reservation = AuditEntries.AppendedSince(host.Data, 0).Last(entry => AuditEntries.Kind(entry) == "reservation");
        Assert.Equal(newest + 1 + PollNumbers.ReservationSpan, reservation.GetProperty("below").GetInt64());

        second.AnswerPoll(host.Server.Ordinary());
        ServerExchange third = await host.NextPollAsync();

        Assert.Equal(newest + 2, third.ReadPoll().Number);
    }

    // The next number would pass 2⁶²: the agent records its reservation, the record of this first stop, and stops; a
    // later start finds a reservation that leaves no number, records started with an error, and polls nothing.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task An_answer_to_an_older_poll_whose_newest_is_2_to_the_62_stops_the_agent_for_good()
    {
        PairedFolder folder = PairedFolder.Create();
        EarlierRun.Append(folder, Reservation(EarlierRun.Time, PairedFolder.Agent, TestTimes.Start.UtcTicks));
        DateTimeOffset now = TestTimes.Start;

        await using (TestHost host = TestHost.Start(folder, new FakeClock(now)))
        {
            (await host.NextPollAsync()).AnswerPoll(host.Server.Older(OutpostProtocol.MaxPollNumber));

            // The number is taken for the next poll, after the wait that an older poll's answer gives.
            Task delay = host.WaitForTimerAsync(now + TimeSpan.FromSeconds(3));

            if (await Task.WhenAny(host.Exit, delay) == delay)
            {
                host.Clock.Advance(TimeSpan.FromSeconds(3));
            }

            Assert.Equal(AgentExitCodes.Error, await host.ExitAsync());
            JsonElement last = AuditEntries.AppendedSince(host.Data, 0)[^1];
            Assert.Equal("reservation", AuditEntries.Kind(last));
            Assert.Equal(OutpostProtocol.MaxPollNumber + 1 + PollNumbers.ReservationSpan, last.GetProperty("below").GetInt64());
            Assert.Equal(1, host.Server.Count(OutpostRequestKindEnum.Poll));
        }

        int count = folder.Data.Changes.Count;

        await using TestHost later = TestHost.Start(folder, new FakeClock(now.AddMinutes(1)));

        Assert.Equal(AgentExitCodes.Error, await later.ExitAsync());
        JsonElement started = AuditEntries.AppendedSince(folder.Data, count)[0];
        Assert.Equal("started", AuditEntries.Kind(started));
        Assert.False(string.IsNullOrEmpty(started.GetProperty("error").GetString()));
        Assert.Equal(0, later.Server.Count(OutpostRequestKindEnum.Poll));
    }
}
