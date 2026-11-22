using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.Store.StoreCommands;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// Removing commands from the store after their retention, against a real PostgreSQL with the fake clock (concept rule
// 4): a command whose outcome was set a retention ago or more, and one with no outcome whose end by time was due that
// long ago, which is removed as if it had ended when due, without being ended first. The purge goes on until it has
// removed every such command, however many, and gives their number. Each test works in a schema of its own.
[Trait("Requires", "PostgreSQL")]
public sealed class CommandPurgeTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // The default of OutcomeRetention.
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    // The two commands expired a second apart.
    [Fact]
    public async Task The_purge_removes_the_commands_that_ended_a_retention_ago_or_more_and_gives_their_number()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "purge_ended", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 0, null)], []), cancellationToken);
        CommandRow first = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        CommandRow second = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(first, cancellationToken));
        Assert.NotNull(await store.CreateCommandAsync(second, cancellationToken));
        clock.Advance(TakeTimeout);
        Assert.NotNull((await store.PassCommandAsync(first.Id, cancellationToken)).Outcome);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull((await store.PassCommandAsync(second.Id, cancellationToken)).Outcome);
        clock.Advance(Retention - TimeSpan.FromSeconds(1));

        Assert.Equal(1, await store.PurgeCommandsAsync(Retention, cancellationToken));
        Assert.Equal<Guid>([second.Id], (await schema.ListCommandsAsync(cancellationToken)).Select(row => row.Id));

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1, await store.PurgeCommandsAsync(Retention, cancellationToken));
        Assert.Empty(await schema.ListCommandsAsync(cancellationToken));
    }

    // The command was never ended: no poll took it by its deadline, or its agent stayed away for its run limit after
    // the end of its lease. A second before its end was due a retention ago, the purge leaves it as it is.
    [Theory]
    [InlineData("not_taken")]
    [InlineData("taken")]
    public async Task The_purge_removes_a_command_with_no_outcome_once_its_end_by_time_was_due_a_retention_ago(string phase)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, $"purge_due_{phase}", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, null)], []), cancellationToken);
        CommandRow read = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));
        DateTimeOffset due = read.Deadline;

        if (phase == "taken")
        {
            await agent.PollAndTakeAsync([Serving("accounting", 1, null)], [], cancellationToken);
            due = clock.GetUtcNow() + StoreAgent.Hold + StoreAgent.LeaseGrace + ReadRunLimit;
        }

        clock.Advance(due + Retention - TimeSpan.FromSeconds(1) - clock.GetUtcNow());

        Assert.Equal(0, await store.PurgeCommandsAsync(Retention, cancellationToken));

        CommandRow? row = await schema.FindCommandAsync(read.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Null(row.OutcomeKind);
        Assert.Null(row.EndedAt);

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1, await store.PurgeCommandsAsync(Retention, cancellationToken));
        Assert.Empty(await schema.ListCommandsAsync(cancellationToken));
    }

    // More commands than one statement of the purge removes, with no outcome and no poll that took them. They are
    // inserted at once, as the store creates them, rather than created one by one.
    [Fact]
    public async Task The_purge_removes_every_command_that_is_due_however_many_there_are()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "purge_many", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 0, null)], []), cancellationToken);
        CommandRow[] commands =
        [
            .. Enumerable.Range(0, 1_001)
                .Select(_ => Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow())),
        ];
        await schema.InsertCommandsAsync(commands, cancellationToken);
        clock.Advance(TakeTimeout + Retention);

        Assert.Equal(1_001, await store.PurgeCommandsAsync(Retention, cancellationToken));
        Assert.Empty(await schema.ListCommandsAsync(cancellationToken));
    }
}
