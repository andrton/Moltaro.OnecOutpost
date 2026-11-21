using Moltaro.OnecOutpost.Server.Tests.Loopback;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Server.Tests.Purge;

// The purge on a node that runs, with the fake clock: the hosted service that AddOutpostServer registers removes the
// pairing tokens that expired unused, with their pending agents, once when the host starts and then after every
// PurgeInterval. A round arms the delay of the next one, on the clock of the options, once it has ended, so the test
// waits for that delay before it moves the clock and before it reads what a round left.
[Trait("Requires", "PostgreSQL")]
public sealed class PurgeTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_purge_removes_a_token_that_expired_unused_with_its_pending_agent()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TimeSpan interval = TimeSpan.FromMinutes(2);
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "purge_of_unused_token",
            options =>
            {
                options.PairingTokenLifetime = TimeSpan.FromMinutes(1);
                options.PurgeInterval = interval;
            },
            cancellationToken);
        DateTimeOffset startedAt = server.Clock.GetUtcNow();
        await server.WaitForTimerAsync(startedAt + interval, cancellationToken);
        OutpostPairingToken token = await server.Server.CreatePairingTokenAsync("Unused agent", cancellationToken);
        Assert.Contains(await server.Server.GetAgentsAsync(cancellationToken), agent => agent.Id == token.AgentId);

        server.Clock.Advance(interval);
        await server.WaitForTimerAsync(startedAt + (2 * interval), cancellationToken);

        IReadOnlyList<OutpostAgent> agents = await server.Server.GetAgentsAsync(cancellationToken);

        Assert.DoesNotContain(agents, agent => agent.Id == token.AgentId);
        Assert.Null(await server.Schema.FindTokenAsync(token.AgentId, cancellationToken));
    }
}
