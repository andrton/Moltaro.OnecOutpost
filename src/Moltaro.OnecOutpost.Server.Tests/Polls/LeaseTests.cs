using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using static Moltaro.OnecOutpost.TestSupport.Loopback.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Polls;

// Whether an agent shows as connected, over loopback HTTP with the fake clock (concept rule 6, §6.2 of
// docs/protocol.md): the lease of its newest poll ends at the earlier of the recording plus the hold and the lease
// grace, and the poll's signed time plus ClockSkew, the hold and the grace. The polls carry a poll timeout of 20
// seconds, below the option of 25, so that the hold is the poll's; the grace is 10 seconds and the skew 2 minutes.
[Trait("Requires", "PostgreSQL")]
public sealed class LeaseTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const int ClockSkewSeconds = 120;

    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan LeaseGrace = TimeSpan.FromSeconds(10);

    // A poll on time grants its hold plus the lease grace. A poll whose signed time is behind the node's by more than
    // ClockSkew grants less, by the part of its lag beyond ClockSkew.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(ClockSkewSeconds + 7, 7)]
    public async Task A_poll_keeps_its_agent_connected_for_its_hold_and_the_grace_less_the_part_of_its_lag_beyond_the_skew(
        int lagSeconds,
        int lessSeconds)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync($"lease_lag_{lagSeconds}", cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        DateTimeOffset recordedAt = server.Clock.GetUtcNow();
        TimeSpan lease = Hold + LeaseGrace - TimeSpan.FromSeconds(lessSeconds);
        OutpostPollRequest poll = agent.NextPoll(Hold, [ReadOnly("accounting")]) with
        {
            Time = recordedAt - TimeSpan.FromSeconds(lagSeconds),
        };

        Task<AgentAnswer> answer = agent.PollAsync(poll, cancellationToken);
        await server.WaitForTimerAsync(recordedAt + Hold, cancellationToken);

        Assert.True(await server.IsConnectedAsync(agent.Id, cancellationToken));

        server.Clock.Advance(lease - TimeSpan.FromMicroseconds(1));
        agent.ReadPollAnswer(poll, await answer);

        Assert.True(await server.IsConnectedAsync(agent.Id, cancellationToken));

        server.Clock.Advance(TimeSpan.FromMicroseconds(1));

        Assert.False(await server.IsConnectedAsync(agent.Id, cancellationToken));
    }

    // Behind by one second more than the skew, the hold and the grace, its lease has ended when the poll is recorded.
    [Fact]
    public async Task An_agent_whose_clock_is_behind_by_more_than_the_skew_the_hold_and_the_grace_never_shows_as_connected()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await StartAsync("lease_never", cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        DateTimeOffset recordedAt = server.Clock.GetUtcNow();
        TimeSpan lag = TimeSpan.FromSeconds(ClockSkewSeconds) + Hold + LeaseGrace + TimeSpan.FromSeconds(1);
        OutpostPollRequest poll = agent.NextPoll(Hold, [ReadOnly("accounting")]) with { Time = recordedAt - lag };

        Task<AgentAnswer> answer = agent.PollAsync(poll, cancellationToken);
        await server.WaitForTimerAsync(recordedAt + Hold, cancellationToken);

        Assert.False(await server.IsConnectedAsync(agent.Id, cancellationToken));

        server.Clock.Advance(Hold);
        agent.ReadPollAnswer(poll, await answer);

        Assert.False(await server.IsConnectedAsync(agent.Id, cancellationToken));
    }

    private Task<LoopbackServer> StartAsync(string schemaName, CancellationToken cancellationToken)
    {
        return LoopbackServer.StartAsync(
            postgres,
            schemaName,
            options =>
            {
                options.PollTimeout = TimeSpan.FromSeconds(25);
                options.LeaseGrace = LeaseGrace;
                options.ClockSkew = TimeSpan.FromSeconds(ClockSkewSeconds);
            },
            cancellationToken);
    }
}
