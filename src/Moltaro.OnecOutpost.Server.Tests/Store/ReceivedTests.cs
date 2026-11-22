using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Store;
using static Moltaro.OnecOutpost.Server.Tests.Store.StoreCommands;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// The list of received commands of a poll in the store, against a real PostgreSQL with the fake clock (concept rules
// 7, 10 and 11). The newest poll makes its agent's handed-out commands that it lists Received. Of those it leaves out,
// it makes Expired the ones with no outcome or with one the server set, Unknown or Failed, once its signed time is
// later than their take deadline plus their skew: the agent's clock decides, since by it the agent refuses such a
// command itself. A Received command is never Expired, an older poll changes no command, and an identity that names
// no command of the agent changes nothing. Each test works in a schema of its own.
[Trait("Requires", "PostgreSQL")]
public sealed class ReceivedTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // The workers of the agent run the commands, so the poll has no free worker.
    [Fact]
    public async Task A_newest_poll_makes_the_handed_out_commands_that_it_lists_Received()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "received_listed", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow read = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        CommandRow write = Create(agent, "accounting", OutpostModeEnum.Write, "invoice-42", clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));
        Assert.NotNull(await store.CreateCommandAsync(write, cancellationToken));
        await agent.PollAndTakeAsync([Serving("accounting", 1, 1)], [], cancellationToken);

        PollRecord record = await agent.RecordAsync(
            agent.NextPoll([Serving("accounting", 0, 0)], [read.Id, write.Id]),
            cancellationToken);

        Assert.Empty(record.Expired);
        Assert.Null(record.State.Newest);

        foreach (CommandRow command in (CommandRow[])[read, write])
        {
            CommandRow? row = await schema.FindCommandAsync(command.Id, cancellationToken);
            Assert.NotNull(row);
            Assert.Equal(CommandPhaseEnum.Received, row.Phase);
            Assert.Null(row.OutcomeKind);
        }
    }

    // Concept rule 7: the agent's clock is ahead of the node's, which has not reached the take deadline. The command's
    // deadline plus its skew is 150 seconds after it was issued.
    [Fact]
    public async Task A_handed_out_command_that_the_newest_poll_leaves_out_becomes_Expired_once_the_signed_time_of_the_poll_is_later_than_its_deadline_plus_its_skew()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "received_left_out_expired", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        DateTimeOffset issued = clock.GetUtcNow();
        CommandRow read = await HandOutReadAsync(store, agent, "accounting", issued, cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(10));

        PollRecord record = await agent.RecordAsync(
            agent.NextPoll([Serving("accounting", 1, null)], [], issued + TimeSpan.FromSeconds(151)),
            cancellationToken);

        Assert.Equal<Guid>([read.Id], record.Expired);

        CommandRow? row = await schema.FindCommandAsync(read.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(CommandPhaseEnum.HandedOut, row.Phase);
        Assert.Equal(OutpostOutcomeKindEnum.Expired, row.OutcomeKind);
        Assert.Null(row.OutcomeCode);
        Assert.Null(row.Outcome);
        Assert.Equal(clock.GetUtcNow(), row.EndedAt);
    }

    // Concept rule 7: the node's clock is ten minutes past the command's deadline plus its skew, and the poll's signed
    // time is exactly at that moment, 150 seconds after the command was issued. The command may still reach the agent
    // from a held-back answer, and the agent would still accept it.
    [Fact]
    public async Task A_handed_out_command_that_the_newest_poll_leaves_out_stays_HandedOut_until_the_signed_time_of_the_poll_is_later_than_its_deadline_plus_its_skew()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "received_left_out_kept", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        DateTimeOffset issued = clock.GetUtcNow();
        CommandRow read = await HandOutReadAsync(store, agent, "accounting", issued, cancellationToken);
        clock.Advance(TimeSpan.FromSeconds(750));

        PollRecord record = await agent.RecordAsync(
            agent.NextPoll([Serving("accounting", 1, null)], [], issued + TimeSpan.FromSeconds(150)),
            cancellationToken);

        Assert.Empty(record.Expired);

        CommandRow? row = await schema.FindCommandAsync(read.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(CommandPhaseEnum.HandedOut, row.Phase);
        Assert.Null(row.OutcomeKind);
        Assert.Null(row.EndedAt);
    }

    // Concept rules 3 and 11: the command reached its agent, and Expired would free a key whose Write may still run.
    // The agent no longer lists it, as after a restore of its audit log, with a time long past its deadline plus its
    // skew.
    [Fact]
    public async Task A_received_command_is_never_Expired()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "received_never_expired", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow write = Create(agent, "accounting", OutpostModeEnum.Write, "invoice-42", clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(write, cancellationToken));
        await agent.PollAndTakeAsync([Serving("accounting", 1, 1)], [], cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 0)], [write.Id]), cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(10));

        PollRecord record = await agent.RecordAsync(
            agent.NextPoll([Serving("accounting", 1, 1)], []),
            cancellationToken);

        Assert.Empty(record.Expired);

        CommandRow? row = await schema.FindCommandAsync(write.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(CommandPhaseEnum.Received, row.Phase);
        Assert.Null(row.OutcomeKind);
    }

    // Concept rule 11: the server set the outcome when the agent stayed away for the command's run limit after its
    // lease. The agent comes back a second later with a poll that leaves the command out and is signed past its
    // deadline plus its skew, which proves the command never reached it: Expired replaces the outcome, from the moment
    // of that poll.
    [Theory]
    [InlineData("write")]
    [InlineData("read")]
    public async Task A_newest_poll_that_proves_a_command_never_reached_its_agent_turns_the_outcome_the_server_set_into_Expired(
        string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(
            postgres,
            $"received_replaces_{mode}",
            cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        bool write = mode == "write";
        CommandRow command = write
            ? Create(agent, "accounting", OutpostModeEnum.Write, "invoice-42", clock.GetUtcNow())
            : Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(command, cancellationToken));
        await agent.PollAndTakeAsync([Serving("accounting", 1, 1)], [], cancellationToken);
        clock.Advance(StoreAgent.Hold + StoreAgent.LeaseGrace + (write ? WriteRunLimit : ReadRunLimit));

        OutpostOutcome? set = (await store.PassCommandAsync(command.Id, cancellationToken)).Outcome;

        Assert.Equal(write ? OutpostOutcomeKindEnum.Unknown : OutpostOutcomeKindEnum.Failed, set?.Kind);

        clock.Advance(TimeSpan.FromSeconds(1));

        PollRecord record = await agent.RecordAsync(
            agent.NextPoll([Serving("accounting", 1, 1)], []),
            cancellationToken);

        Assert.Equal<Guid>([command.Id], record.Expired);

        CommandRow? row = await schema.FindCommandAsync(command.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(OutpostOutcomeKindEnum.Expired, row.OutcomeKind);
        Assert.Null(row.OutcomeCode);
        Assert.Null(row.Outcome);
        Assert.Equal(clock.GetUtcNow(), row.EndedAt);
    }

    // Concept rule 11: only an outcome the server set is replaced. The agent's own outcome is Unknown, a kind that the
    // server sets too; the agent no longer lists the command, since the server confirmed that outcome.
    [Fact]
    public async Task A_command_with_the_outcome_of_its_agent_is_not_Expired_by_a_poll_that_leaves_it_out()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "received_agent_outcome", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow write = Create(agent, "accounting", OutpostModeEnum.Write, "invoice-42", clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(write, cancellationToken));
        await agent.PollAndTakeAsync([Serving("accounting", 1, 1)], [], cancellationToken);
        Assert.True(await agent.SendOutcomeAsync(
            write.Id,
            AgentOutcomes.Unknown(OutpostOutcomeCodes.AgentStopped),
            cancellationToken));
        clock.Advance(TimeSpan.FromMinutes(10));

        PollRecord record = await agent.RecordAsync(
            agent.NextPoll([Serving("accounting", 1, 1)], []),
            cancellationToken);

        Assert.Empty(record.Expired);

        CommandRow? row = await schema.FindCommandAsync(write.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(OutpostOutcomeKindEnum.Unknown, row.OutcomeKind);
        Assert.Equal(OutpostOutcomeCodes.AgentStopped, row.OutcomeCode);
        Assert.NotNull(row.Outcome);
    }

    // Concept rule 10: the older poll lists one of two handed-out commands and leaves the other out, with a time past
    // their deadline plus their skew. As the newest it would make the first Received and the second Expired.
    [Fact]
    public async Task An_older_poll_changes_no_command()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "received_older_poll", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        OutpostPollRequest first = agent.NextPoll([Serving("accounting", 2, null)], []);
        await agent.RecordAsync(first, cancellationToken);
        CommandRow listed = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        CommandRow leftOut = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(listed, cancellationToken));
        Assert.NotNull(await store.CreateCommandAsync(leftOut, cancellationToken));
        await agent.PollAndTakeAsync([Serving("accounting", 2, null)], [], cancellationToken);
        SchemaSnapshot before = await schema.TakeSnapshotAsync(cancellationToken);
        OutpostPollRequest older = first with
        {
            Time = first.Time + TimeSpan.FromSeconds(151),
            Received = [listed.Id],
        };

        PollRecord record = await agent.RecordAsync(older, cancellationToken);

        Assert.Empty(record.Expired);
        Assert.Equal(agent.Number, record.State.Newest);
        Assert.Equal(before.Lines, (await schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // The other agent's commands were handed out to it. This agent's newest poll lists one of them and an identity
    // that names no command, and leaves the other out, with a time past their deadline plus their skew.
    [Fact]
    public async Task A_newest_poll_changes_no_command_of_another_agent()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "received_other_agent", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent other = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await other.RecordAsync(other.NextPoll([Serving("warehouse", 2, null)], []), cancellationToken);
        CommandRow listed = Create(other, "warehouse", OutpostModeEnum.Read, null, clock.GetUtcNow());
        CommandRow leftOut = Create(other, "warehouse", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(listed, cancellationToken));
        Assert.NotNull(await store.CreateCommandAsync(leftOut, cancellationToken));
        await other.PollAndTakeAsync([Serving("warehouse", 2, null)], [], cancellationToken);

        PollRecord record = await agent.RecordAsync(
            agent.NextPoll(
                [Serving("accounting", 1, null)],
                [listed.Id, Guid.NewGuid()],
                clock.GetUtcNow() + TimeSpan.FromSeconds(151)),
            cancellationToken);

        Assert.Empty(record.Expired);

        foreach (CommandRow command in (CommandRow[])[listed, leftOut])
        {
            CommandRow? row = await schema.FindCommandAsync(command.Id, cancellationToken);
            Assert.NotNull(row);
            Assert.Equal(CommandPhaseEnum.HandedOut, row.Phase);
            Assert.Null(row.OutcomeKind);
        }
    }
}
