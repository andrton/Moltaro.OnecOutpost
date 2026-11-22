using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Store;
using static Moltaro.OnecOutpost.Server.Tests.Store.StoreCommands;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// The outcomes of agents in the store, against a real PostgreSQL with the fake clock (concept rule 11, §6.3 of
// docs/protocol.md). The first outcome of a command is kept as it came, and only from the agent the command was handed
// to. A later one, one from another agent, one for a command that no poll took and one for a command the store does
// not know change nothing. The one exception is an outcome the server set, Unknown or Failed, which the outcome of the
// agent replaces. The checks of an outcome find the mode and the outcome limit of its command. Each test works in a
// schema of its own.
[Trait("Requires", "PostgreSQL")]
public sealed class OutcomeTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // The outcome comes while the command is handed out, before the agent's next poll lists it, or after that poll.
    [Theory]
    [InlineData("handed_out")]
    [InlineData("received")]
    public async Task The_first_outcome_of_a_command_from_the_agent_it_was_handed_to_is_kept_as_it_came(string phase)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, $"outcome_kept_{phase}", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        CommandRow read = await HandOutReadAsync(store, agent, "accounting", clock.GetUtcNow(), cancellationToken);
        CommandPhaseEnum expectedPhase = CommandPhaseEnum.HandedOut;

        if (phase == "received")
        {
            await agent.RecordAsync(agent.NextPoll([Serving("accounting", 0, null)], [read.Id]), cancellationToken);
            expectedPhase = CommandPhaseEnum.Received;
        }

        clock.Advance(TimeSpan.FromSeconds(5));
        OutpostOutcomeMessage outcome = AgentOutcomes.Succeeded("""{"rows":3}""");
        byte[] bytes = OutpostCodec.Write(outcome);

        bool kept = await store.KeepOutcomeAsync(agent.Id, read.Id, outcome, bytes, cancellationToken);

        Assert.True(kept);

        CommandRow? row = await schema.FindCommandAsync(read.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(expectedPhase, row.Phase);
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, row.OutcomeKind);
        Assert.Null(row.OutcomeCode);
        Assert.Equal(bytes, row.Outcome);
        Assert.Equal(clock.GetUtcNow(), row.EndedAt);
    }

    // Concept rules 5 and 11: a Read that failed stays failed, and a Write whose agent reported it Unknown stays
    // Unknown, a kind the server sets too, when a later outcome claims more.
    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task A_later_outcome_of_a_command_changes_nothing(string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, $"outcome_later_{mode}", cancellationToken);
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
        OutpostOutcomeMessage first = write
            ? AgentOutcomes.Unknown(OutpostOutcomeCodes.AgentStopped)
            : AgentOutcomes.ScriptError("Division by zero");
        Assert.True(await agent.SendOutcomeAsync(command.Id, first, cancellationToken));
        clock.Advance(TimeSpan.FromSeconds(5));
        SchemaSnapshot before = await schema.TakeSnapshotAsync(cancellationToken);

        Assert.False(await agent.SendOutcomeAsync(command.Id, AgentOutcomes.Succeeded("1"), cancellationToken));

        Assert.Equal(before.Lines, (await schema.TakeSnapshotAsync(cancellationToken)).Lines);
        Assert.Equal(
            OutpostCodec.Write(first),
            (await schema.FindCommandAsync(command.Id, cancellationToken))?.Outcome);
    }

    // Concept rule 11: only the agent that the command was handed to reports its outcome. The command stays open for
    // that agent's outcome.
    [Fact]
    public async Task An_outcome_from_another_agent_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "outcome_other_agent", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent other = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await other.RecordAsync(other.NextPoll([Serving("warehouse", 1, null)], []), cancellationToken);
        CommandRow read = await HandOutReadAsync(store, agent, "accounting", clock.GetUtcNow(), cancellationToken);
        SchemaSnapshot before = await schema.TakeSnapshotAsync(cancellationToken);

        Assert.False(await other.SendOutcomeAsync(read.Id, AgentOutcomes.Succeeded("1"), cancellationToken));

        Assert.Equal(before.Lines, (await schema.TakeSnapshotAsync(cancellationToken)).Lines);
        Assert.True(await agent.SendOutcomeAsync(read.Id, AgentOutcomes.Succeeded("2"), cancellationToken));
    }

    // Concept rule 11: a command that no poll took was handed to no agent.
    [Fact]
    public async Task An_outcome_for_a_command_that_no_poll_took_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "outcome_not_taken", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 0, null)], []), cancellationToken);
        CommandRow read = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));
        SchemaSnapshot before = await schema.TakeSnapshotAsync(cancellationToken);

        Assert.False(await agent.SendOutcomeAsync(read.Id, AgentOutcomes.Succeeded("1"), cancellationToken));

        Assert.Equal(before.Lines, (await schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // Concept rule 11: the server confirms an outcome for a command it no longer knows, such as one removed after its
    // retention, and keeps nothing of it.
    [Fact]
    public async Task An_outcome_for_a_command_the_store_does_not_know_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "outcome_unknown_command", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await HandOutReadAsync(store, agent, "accounting", clock.GetUtcNow(), cancellationToken);
        SchemaSnapshot before = await schema.TakeSnapshotAsync(cancellationToken);

        Assert.False(await agent.SendOutcomeAsync(Guid.NewGuid(), AgentOutcomes.Succeeded("1"), cancellationToken));

        Assert.Equal(before.Lines, (await schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // Concept rule 11: the server set the outcome when the agent stayed away for the command's run limit after its
    // lease, and the agent comes back with the real outcome. It replaces the server's, and its retention counts from
    // that moment.
    [Theory]
    [InlineData("write")]
    [InlineData("read")]
    public async Task The_outcome_of_the_agent_replaces_an_outcome_the_server_set_Unknown_disconnected_or_Failed_lost(
        string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, $"outcome_replaces_{mode}", cancellationToken);
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
        Assert.Equal(
            write ? OutpostOutcomeKindEnum.Unknown : OutpostOutcomeKindEnum.Failed,
            (await store.PassCommandAsync(command.Id, cancellationToken)).Outcome?.Kind);
        clock.Advance(TimeSpan.FromSeconds(5));
        OutpostOutcomeMessage outcome = AgentOutcomes.Succeeded("""{"rows":3}""");

        Assert.True(await agent.SendOutcomeAsync(command.Id, outcome, cancellationToken));

        CommandRow? row = await schema.FindCommandAsync(command.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, row.OutcomeKind);
        Assert.Null(row.OutcomeCode);
        Assert.Equal(OutpostCodec.Write(outcome), row.Outcome);
        Assert.Equal(clock.GetUtcNow(), row.EndedAt);
        Assert.Equal(
            OutpostOutcomeKindEnum.Succeeded,
            (await store.PassCommandAsync(command.Id, cancellationToken)).Outcome?.Kind);
    }

    // Concept rules 4 and 11: Expired, which a poll set when it proved that the command never reached the agent, freed
    // the caller key, so no outcome replaces it.
    [Fact]
    public async Task An_outcome_for_a_command_that_ended_as_Expired_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "outcome_after_expired", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        CommandRow read = await HandOutReadAsync(store, agent, "accounting", clock.GetUtcNow(), cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(3));
        PollRecord record = await agent.RecordAsync(
            agent.NextPoll([Serving("accounting", 1, null)], []),
            cancellationToken);
        Assert.Equal<Guid>([read.Id], record.Expired);
        SchemaSnapshot before = await schema.TakeSnapshotAsync(cancellationToken);

        Assert.False(await agent.SendOutcomeAsync(read.Id, AgentOutcomes.Succeeded("1"), cancellationToken));

        Assert.Equal(before.Lines, (await schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    [Fact]
    public async Task FindCommandLimitsAsync_gives_the_mode_and_the_outcome_limit_of_a_command_and_null_for_one_the_store_does_not_know()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "command_limits", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow write = Create(
            agent,
            "accounting",
            OutpostModeEnum.Write,
            "invoice-42",
            clock.GetUtcNow(),
            outcomeLimit: 65_536);
        Assert.NotNull(await store.CreateCommandAsync(write, cancellationToken));

        Assert.Equal(
            new CommandLimits(OutpostModeEnum.Write, 65_536),
            await store.FindCommandLimitsAsync(write.Id, cancellationToken));
        Assert.Null(await store.FindCommandLimitsAsync(Guid.NewGuid(), cancellationToken));
    }

    // The race of two outcomes of one command, driven to a known order as the race of two claims of a key is: another
    // connection holds the command's row until both outcomes are seen waiting for it, and then lets it go unchanged.
    // Which outcome the store keeps is not the subject; that it keeps one, as it came, and the other changes nothing,
    // is.
    [Fact]
    public async Task Of_two_outcomes_of_one_command_one_is_kept_and_the_other_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "outcomes_at_once", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        CommandRow read = await HandOutReadAsync(store, agent, "accounting", clock.GetUtcNow(), cancellationToken);
        OutpostOutcomeMessage firstOutcome = AgentOutcomes.Succeeded("1");
        OutpostOutcomeMessage secondOutcome = AgentOutcomes.Succeeded("2");

        await using HeldLock rowLock = await HeldLock.TakeAsync(
            schema.ConnectionString,
            $"SELECT id FROM {schema.Name}.commands WHERE id = '{read.Id}' FOR UPDATE",
            cancellationToken);

        Task<bool> first = agent.SendOutcomeAsync(read.Id, firstOutcome, cancellationToken);
        Task<bool> second = agent.SendOutcomeAsync(read.Id, secondOutcome, cancellationToken);

        await rowLock.WaitForAsync([first, second], cancellationToken);
        await rowLock.ReleaseAsync(cancellationToken);

        bool firstKept = await first;
        bool secondKept = await second;

        Assert.NotEqual(firstKept, secondKept);
        Assert.Equal(
            OutpostCodec.Write(firstKept ? firstOutcome : secondOutcome),
            (await schema.FindCommandAsync(read.Id, cancellationToken))?.Outcome);
    }
}
