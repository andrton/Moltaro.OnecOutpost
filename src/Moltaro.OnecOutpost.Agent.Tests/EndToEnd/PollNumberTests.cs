using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Polls;
using Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Server;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.EndToEnd;

// Poll numbers across restarts, end to end against the server of S5 (concept rule 10, §6.2 and §13 of
// docs/protocol.md): only the newest poll takes commands, and the reservations of the audit log keep every poll after a
// restart above every poll before it, whatever the clock did. A server that answers a poll as older names its newest,
// and the agent goes on above it after a tenth of retryDelayMax. A log that was cleared is found by the polls the agent
// counts from 1, and stops the agent for good, so that nothing it had received runs twice.
[Trait("Requires", "PostgreSQL")]
public sealed class PollNumberTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // The second run's clock is a minute behind every poll of the first; within the skew, so its commands run. The node's
    // clock does not move, so the lease of the first run's last poll still runs when the second run's Read is made: the
    // node creates it, and only a poll of the second run that is newer than every poll before can take it.
    [Fact]
    public async Task After_a_restart_with_the_clock_set_back_the_first_poll_is_above_every_poll_before_and_takes_commands()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(postgres, "agent_numbers_restart", cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(server, proxy, cancellationToken);

        await using (HostRun first = agent.Start(server.Clock))
        {
            await server.WaitForTimerAsync(server.Clock.GetUtcNow() + server.Options.PollTimeout, cancellationToken);
            Assert.Equal(OutpostOutcomeKindEnum.Succeeded, (await ReadAsync(server, agent, cancellationToken)).Kind);
            Assert.Equal(AgentExitCodes.Stopped, await first.StopAsync());
        }

        long before = HighestReservation(agent);

        await using HostRun second = agent.Start(new FakeClock(server.Clock.GetUtcNow().AddMinutes(-1)));

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, (await ReadAsync(server, agent, cancellationToken)).Kind);
        Assert.True(HighestReservation(agent) >= before + PollNumbers.ReservationSpan);
        Assert.Equal(AgentExitCodes.Stopped, await second.StopAsync());
    }

    // The log holds a reservation of an earlier run, ten minutes old, and is restored after a run that polled above it:
    // a log restored from an older copy (§13). The next run's clock is a minute behind the node's newest number, so its
    // first poll is older; it waits a tenth of retryDelayMax by its own clock, and its next poll, above the newest that
    // the answer named, takes commands.
    [Fact]
    public async Task With_its_reservation_and_the_clock_set_behind_the_newest_number_the_first_poll_is_older_and_the_next_takes_commands()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(postgres, "agent_numbers_behind", cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(server, proxy, cancellationToken);
        DateTimeOffset now = server.Clock.GetUtcNow();
        DateTimeOffset earlier = now.AddMinutes(-10);
        agent.Data.Audit.Seed(FileOf(earlier), Reservation(earlier, agent.Id, earlier.UtcTicks));
        Dictionary<string, byte[]> copy = agent.Data.Audit.Names().ToDictionary(name => name, agent.Data.Audit.Bytes);

        await using (HostRun first = agent.Start(server.Clock))
        {
            await server.WaitForTimerAsync(now + server.Options.PollTimeout, cancellationToken);
            Assert.Equal(AgentExitCodes.Stopped, await first.StopAsync());
        }

        foreach (string name in agent.Data.Audit.Names())
        {
            agent.Data.Audit.Unseed(name);
        }

        foreach ((string name, byte[] bytes) in copy)
        {
            agent.Data.Audit.Seed(name, bytes);
        }

        DateTimeOffset behind = now.AddMinutes(-1);
        FakeClock clock = new(behind);
        await using HostRun second = agent.Start(clock);
        TimeSpan tenth = TimeSpan.FromSeconds(3);

        await clock.WaitForTimerAsync(behind + tenth, cancellationToken).WaitAsync(LoopbackServer.Deadline, cancellationToken);
        Assert.DoesNotContain(Operations.Since(agent.Data, 0), operation => operation.StartsWith("received ", StringComparison.Ordinal));
        clock.Advance(tenth);

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, (await ReadAsync(server, agent, cancellationToken)).Kind);
        Assert.Equal(AgentExitCodes.Stopped, await second.StopAsync());
    }

    // §6.2 and §13: an agent with no reservation of its identity, as after pairing, numbers its first poll 1, which is
    // the newest and takes a Write. The log and the outbox are then cleared, with the Write received and not ended. The
    // next run's first poll, 1 again, is answered as older: the agent records a reservation that leaves no number and
    // stops without running anything, and the Write is not handed out to it again. Every later start records started
    // with an error.
    [Fact]
    public async Task A_cleared_log_is_found_by_the_first_poll_and_stops_the_agent_for_good_without_running_anything()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(postgres, "agent_numbers_cleared", cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(server, proxy, cancellationToken);
        agent.Workers.Runs = _ => FakeRun.Overruns();

        await using (HostRun first = agent.Start(server.Clock))
        {
            await server.WaitForTimerAsync(server.Clock.GetUtcNow() + server.Options.PollTimeout, cancellationToken);
            JsonElement reservation = AuditEntries.AppendedSince(agent.Data, 0).First(entry => AuditEntries.Kind(entry) == "reservation");
            Assert.Equal(1 + PollNumbers.ReservationSpan, reservation.GetProperty("below").GetInt64());

            _ = await server.StartCallAsync(
                agent.Id,
                () => TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken),
                cancellationToken);
            await agent.Data.WaitForAsync(Operations.Is("run accounting write 1"));
            Assert.Equal(AgentExitCodes.Stopped, await first.StopAsync());
        }

        foreach (string name in agent.Data.Audit.Names())
        {
            agent.Data.Audit.Unseed(name);
        }

        foreach (string name in agent.Data.Outcomes.Names())
        {
            agent.Data.Outcomes.Unseed(name);
        }

        int count = agent.Data.Changes.Count;

        await using (HostRun second = agent.Start(server.Clock))
        {
            Assert.Equal(AgentExitCodes.Error, await second.ExitAsync());
        }

        List<string> operations = Operations.Since(agent.Data, count);
        Assert.DoesNotContain(operations, operation => operation.StartsWith("received ", StringComparison.Ordinal));
        Assert.DoesNotContain(operations, operation => operation.StartsWith("run ", StringComparison.Ordinal));
        List<JsonElement> reservations = [.. AuditEntries.AppendedSince(agent.Data, count).Where(entry => AuditEntries.Kind(entry) == "reservation")];
        Assert.Equal(1 + PollNumbers.ReservationSpan, reservations[0].GetProperty("below").GetInt64());
        Assert.Equal(OutpostProtocol.MaxPollNumber + 1, reservations[^1].GetProperty("below").GetInt64());

        count = agent.Data.Changes.Count;

        await using (HostRun later = agent.Start(server.Clock))
        {
            Assert.Equal(AgentExitCodes.Error, await later.ExitAsync());
        }

        JsonElement started = AuditEntries.AppendedSince(agent.Data, count)[0];
        Assert.Equal("started", AuditEntries.Kind(started));
        Assert.False(string.IsNullOrEmpty(started.GetProperty("error").GetString()));
    }

    // A Read of the agent's base, made while the agent counts as connected, and its outcome.
    private static async Task<OutpostOutcome> ReadAsync(LoopbackServer server, LoopbackAgent agent, CancellationToken cancellationToken)
    {
        Task<OutpostOutcome> call = await server.StartCallAsync(
            agent.Id,
            () => TestCalls.CallAsync(server.Server, "read", null, cancellationToken),
            cancellationToken);

        return await call;
    }

    private static long HighestReservation(LoopbackAgent agent)
    {
        return AuditEntries.AppendedSince(agent.Data, 0)
            .Where(entry => AuditEntries.Kind(entry) == "reservation")
            .Max(entry => entry.GetProperty("below").GetInt64());
    }
}
