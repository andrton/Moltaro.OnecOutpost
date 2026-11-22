using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.Store.StoreCommands;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// A pass of a waiting call in the store, against a real PostgreSQL with the fake clock (concept rules 7 and 11). It
// ends the command when time made its end due: Expired when no poll took it by its take deadline, by the node's clock;
// Unknown `disconnected` for a Write and Failed `lost` for a Read when it was taken and its agent stayed away for its
// run limit after the end of its lease. Then it reads the command's outcome and the moment time can next end it: the
// deadline while no poll took it, the end of its agent's lease plus its run limit once taken. Each test works in a
// schema of its own.
[Trait("Requires", "PostgreSQL")]
public sealed class CommandPassTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_pass_of_a_command_that_no_poll_took_gives_no_outcome_before_its_deadline_and_the_deadline_as_due()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "pass_before_deadline", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 0, null)], []), cancellationToken);
        CommandRow read = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));
        clock.Advance(TakeTimeout - TimeSpan.FromSeconds(1));

        CommandPass pass = await store.PassCommandAsync(read.Id, cancellationToken);

        Assert.Null(pass.Outcome);
        Assert.Equal(read.Deadline, pass.Due);
        Assert.Null((await schema.FindCommandAsync(read.Id, cancellationToken))?.OutcomeKind);
    }

    // Concept rule 7: a command that no poll ever took expires at its take deadline.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_pass_of_a_command_that_no_poll_took_makes_it_Expired_from_its_deadline_on()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "pass_at_deadline", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 0, null)], []), cancellationToken);
        CommandRow read = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));
        clock.Advance(TakeTimeout);

        CommandPass pass = await store.PassCommandAsync(read.Id, cancellationToken);

        Assert.NotNull(pass.Outcome);
        Assert.Equal(OutpostOutcomeKindEnum.Expired, pass.Outcome.Kind);
        Assert.Equal(read.Id, pass.Outcome.CommandId);

        CommandRow? row = await schema.FindCommandAsync(read.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(CommandPhaseEnum.Waiting, row.Phase);
        Assert.Equal(OutpostOutcomeKindEnum.Expired, row.OutcomeKind);
        Assert.Null(row.OutcomeCode);
        Assert.Null(row.Outcome);
        Assert.Equal(clock.GetUtcNow(), row.EndedAt);
    }

    // Concept rule 7: a command that was handed out expires only on the evidence of its agent's time. The agent polls
    // again later, listing it as received, which moves the end of its lease and with it the moment time can end the
    // command.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_pass_leaves_a_taken_command_past_its_deadline_and_gives_the_end_of_the_agents_lease_plus_its_run_limit_as_due()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "pass_after_deadline", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, null)], []), cancellationToken);
        CommandRow read = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));
        DateTimeOffset takenAt = clock.GetUtcNow();
        await agent.PollAndTakeAsync([Serving("accounting", 1, null)], [], cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));

        CommandPass handedOut = await store.PassCommandAsync(read.Id, cancellationToken);

        Assert.Null(handedOut.Outcome);
        Assert.Equal(takenAt + StoreAgent.Hold + StoreAgent.LeaseGrace + ReadRunLimit, handedOut.Due);

        DateTimeOffset polledAt = clock.GetUtcNow();
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 0, null)], [read.Id]), cancellationToken);

        CommandPass received = await store.PassCommandAsync(read.Id, cancellationToken);

        Assert.Null(received.Outcome);
        Assert.Equal(polledAt + StoreAgent.Hold + StoreAgent.LeaseGrace + ReadRunLimit, received.Due);
        Assert.Null((await schema.FindCommandAsync(read.Id, cancellationToken))?.OutcomeKind);
    }

    // Concept rules 3 and 11: the agent took the command at the last poll it made, and was not seen again. A Write is
    // in its list as received, a Read was only handed out: both count as taken.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("write")]
    [InlineData("read")]
    public async Task A_taken_command_whose_agent_stays_away_for_its_run_limit_after_its_lease_ends_as_Unknown_disconnected_or_Failed_lost(
        string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, $"pass_agent_away_{mode}", cancellationToken);
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
        CommandPhaseEnum phase = CommandPhaseEnum.HandedOut;

        if (write)
        {
            await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 0)], [command.Id]), cancellationToken);
            phase = CommandPhaseEnum.Received;
        }

        TimeSpan runLimit = write ? WriteRunLimit : ReadRunLimit;
        clock.Advance(StoreAgent.Hold + StoreAgent.LeaseGrace + runLimit - TimeSpan.FromSeconds(1));

        Assert.Null((await store.PassCommandAsync(command.Id, cancellationToken)).Outcome);

        clock.Advance(TimeSpan.FromSeconds(1));

        OutpostOutcome? outcome = (await store.PassCommandAsync(command.Id, cancellationToken)).Outcome;

        Assert.NotNull(outcome);
        Assert.Equal(command.Id, outcome.CommandId);
        Assert.Equal(write ? OutpostOutcomeKindEnum.Unknown : OutpostOutcomeKindEnum.Failed, outcome.Kind);
        Assert.Equal(write ? OutpostCodes.Disconnected : OutpostCodes.Lost, outcome.Code);
        Assert.Null(outcome.ErrorText);
        Assert.Empty(outcome.Messages);

        CommandRow? row = await schema.FindCommandAsync(command.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(phase, row.Phase);
        Assert.Equal(outcome.Kind, row.OutcomeKind);
        Assert.Equal(outcome.Code, row.OutcomeCode);
        Assert.Null(row.Outcome);
        Assert.Equal(clock.GetUtcNow(), row.EndedAt);
    }

    // The outcome object of the agent as the store kept it, read for the mode of its command: a Read that succeeded
    // with its result and its messages trimmed, and a Write that failed with the error text of 1C.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("read")]
    [InlineData("write")]
    public async Task A_pass_gives_the_outcome_of_the_agent(string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(
            postgres,
            $"pass_agent_outcome_{mode}",
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
        OutpostOutcomeMessage sent = write
            ? AgentOutcomes.ScriptError("Failed to call the context method (Write)")
            : AgentOutcomes.Succeeded("""{"rows":3}""", messagesTrimmed: true);
        Assert.True(await agent.SendOutcomeAsync(command.Id, sent, cancellationToken));

        OutpostOutcome? outcome = (await store.PassCommandAsync(command.Id, cancellationToken)).Outcome;

        Assert.NotNull(outcome);
        Assert.Equal(command.Id, outcome.CommandId);
        Assert.Equal(write ? OutpostOutcomeKindEnum.Failed : OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(sent.Code, outcome.Code);
        Assert.Equal(sent.Result?.GetRawText(), outcome.Result?.GetRawText());
        Assert.Equal(sent.ErrorText, outcome.ErrorText);
        Assert.Equal(sent.Messages, outcome.Messages);
        Assert.Equal(sent.MessagesTrimmed, outcome.MessagesTrimmed);
    }
}
