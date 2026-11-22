using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Tests.Loopback;
using Moltaro.OnecOutpost.Server.Tests.Store;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Purge;

// The purge on a node that runs, with the fake clock: the hosted service that AddOutpostServer registers removes the
// pairing tokens that expired unused, with their pending agents, and the commands that outlived their retention, once
// when the host starts and then after every PurgeInterval. A round arms the delay of the next one, on the clock of the options, once it has ended, so the test
// waits for that delay before it moves the clock and before it reads what a round left.
[Trait("Requires", "PostgreSQL")]
public sealed class PurgeTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // The one category of the log of the library (the public contract).
    private const string LogCategory = "Moltaro.OnecOutpost.Server";

    [Fact]
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

    // Concept rule 4: an outcome is kept for its retention, and after it the key is free. The Write ends at the start,
    // so the round after one retention removes it, and a repeat with its caller key creates a new command, which the
    // agent's next poll takes.
    [Fact]
    public async Task The_purge_removes_a_command_whose_outcome_outlived_its_retention_and_frees_its_caller_key()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TimeSpan retention = TimeSpan.FromHours(1);
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "purge_of_ended_command",
            options =>
            {
                options.OutcomeRetention = retention;
                options.PurgeInterval = retention;
            },
            cancellationToken);
        DateTimeOffset startedAt = server.Clock.GetUtcNow();
        await server.WaitForTimerAsync(startedAt + retention, cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        (Task<OutpostOutcome> first, OutpostCommandMessage ended) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        await agent.ConfirmOutcomeAsync(ended.Id, AgentOutcomes.Succeeded("null"), cancellationToken);
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, (await first).Kind);

        server.Clock.Advance(retention);
        await server.WaitForTimerAsync(startedAt + (2 * retention), cancellationToken);

        Assert.Empty(await server.Schema.ListCommandsAsync(cancellationToken));

        (Task<OutpostOutcome> repeat, OutpostCommandMessage created) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        await agent.ConfirmOutcomeAsync(created.Id, AgentOutcomes.Succeeded("null"), cancellationToken);

        Assert.NotEqual(ended.Id, created.Id);
        Assert.Equal(created.Id, (await repeat).CommandId);
    }

    // The store of the node is in a schema that was never migrated, so every purge fails, as it does while the store is
    // down. Each failure is one warning, with its exception, under the category of the library, and the next purge
    // still comes after the interval: the host keeps running. The host has no web server, since the purge needs none.
    [Fact]
    public async Task A_purge_that_fails_is_logged_as_one_warning_and_the_next_one_comes_after_the_interval()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        TimeSpan interval = TimeSpan.FromMinutes(2);
        string connectionString = await postgres.GetConnectionStringAsync(cancellationToken);
        FakeClock clock = new();
        RecordedLog log = new();

        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Logging.AddProvider(log);
        builder.Services.AddOutpostServer(options =>
        {
            ServerSetup.Require(options);
            options.ConnectionString = connectionString;
            options.Schema = "purge_never_migrated";
            options.PurgeInterval = interval;
            options.TimeProvider = clock;
        });

        using IHost host = builder.Build();
        DateTimeOffset startedAt = clock.GetUtcNow();
        await host.StartAsync(cancellationToken);

        await WaitForTimerAsync(clock, startedAt + interval, cancellationToken);

        AssertFailedPurges(log, 1);

        clock.Advance(interval);
        await WaitForTimerAsync(clock, startedAt + (2 * interval), cancellationToken);

        AssertFailedPurges(log, 2);

        await host.StopAsync(cancellationToken);
    }

    private static Task WaitForTimerAsync(FakeClock clock, DateTimeOffset dueBy, CancellationToken cancellationToken)
    {
        return clock.WaitForTimerAsync(dueBy, cancellationToken).WaitAsync(LoopbackServer.Deadline, cancellationToken);
    }

    // Every entry of the category of the library is the warning of a failed purge, with its exception.
    private static void AssertFailedPurges(RecordedLog log, int count)
    {
        List<(string Category, LogLevel Level, Exception? Exception)> entries =
            [.. log.Entries.Where(entry => entry.Category == LogCategory)];

        Assert.Equal(count, entries.Count);
        Assert.All(entries, entry => Assert.Equal(LogLevel.Warning, entry.Level));
        Assert.All(entries, entry => Assert.NotNull(entry.Exception));
    }
}
