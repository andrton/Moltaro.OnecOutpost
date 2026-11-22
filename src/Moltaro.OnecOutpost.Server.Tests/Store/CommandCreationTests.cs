using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.Server.Tests.Migrations;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.Store.StoreCommands;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// Creating commands in the store, and finding the one that a base and a caller key name, against a real PostgreSQL
// with the fake clock (concept rule 4): one command per base and caller key among the commands that hold their key,
// which are all but those that ended as Expired or Refused. A repeat finds that command, the same when its mode, its
// script compared exactly and its parameters compared as JSON values are those of the call. A command is created only
// for an agent that is Paired and connected (rules 6, 13). Each test works in a schema of its own.
[Trait("Requires", "PostgreSQL")]
public sealed class CommandCreationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private const string CallerKey = "invoice-42";

    private const string FirstScript = "Result = Parameters.Get(\"number\");";

    private const string FirstParameters = """{"number":"2026/42","lines":[{"amount":10.5}],"count":2}""";

    // Repeats of a first command, a Write with the script and the parameters above: a name for the schema, the mode,
    // the script and the parameters of the repeat, and whether it is the same. The first is equal as JSON values, with
    // its members in another order, its numbers in another notation and a string with an escape; each other differs
    // in one thing.
    public static TheoryData<string, string, string, string, bool> Repeats => new()
    {
        { "equal_as_json", "write", FirstScript, """{"count":2.0,"lines":[{"amount":1.05e1}],"number":"2026\/42"}""", true },
        { "script_with_a_space", "write", FirstScript + " ", FirstParameters, false },
        { "script_in_lower_case", "write", "result = Parameters.Get(\"number\");", FirstParameters, false },
        { "other_value", "write", FirstScript, """{"number":"2026/43","lines":[{"amount":10.5}],"count":2}""", false },
        { "member_fewer", "write", FirstScript, """{"number":"2026/42","lines":[{"amount":10.5}]}""", false },
        { "member_more", "write", FirstScript, """{"number":"2026/42","lines":[{"amount":10.5}],"count":2,"note":null}""", false },
        { "other_mode", "read", FirstScript, FirstParameters, false },
    };

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_create_for_a_connected_agent_stores_the_command_waiting_with_no_outcome_and_gives_it_as_created()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "command_created", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow command = Create(agent, "accounting", OutpostModeEnum.Write, CallerKey, clock.GetUtcNow());

        CommandMatch? match = await store.CreateCommandAsync(command, cancellationToken);

        Assert.Equal(new CommandMatch(command.Id, true, true), match);

        CommandRow? row = await schema.FindCommandAsync(command.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(agent.Id, row.AgentId);
        Assert.Equal("accounting", row.BaseKey);
        Assert.Equal(OutpostModeEnum.Write, row.Mode);
        Assert.Equal(CallerKey, row.CallerKey);
        Assert.Equal(command.Bytes, row.Bytes);
        Assert.Equal(command.Signature, row.Signature);
        Assert.Equal(command.Deadline, row.Deadline);
        Assert.Equal(command.Skew, row.Skew);
        Assert.Equal(command.OutcomeLimit, row.OutcomeLimit);
        Assert.Equal(CommandPhaseEnum.Waiting, row.Phase);
        Assert.Null(row.RunLimit);
        Assert.Null(row.OutcomeKind);
        Assert.Null(row.OutcomeCode);
        Assert.Null(row.Outcome);
        Assert.Null(row.EndedAt);
    }

    // Concept rules 6 and 13: the lease of the agent's poll ends at the moment of the create, or the agent was revoked
    // while its lease runs.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("lease_ended")]
    [InlineData("revoked")]
    public async Task A_create_for_an_agent_that_is_not_connected_gives_null_and_stores_nothing(string why)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(
            postgres,
            $"create_when_{why}",
            cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);

        if (why == "revoked")
        {
            Assert.True((await store.RevokeAgentAsync(agent.Id, cancellationToken)).Revoked);
        }
        else
        {
            clock.Advance(StoreAgent.Hold + StoreAgent.LeaseGrace);
        }

        SchemaSnapshot before = await schema.TakeSnapshotAsync(cancellationToken);
        CommandRow command = Create(agent, "accounting", OutpostModeEnum.Write, CallerKey, clock.GetUtcNow());

        Assert.Null(await store.CreateCommandAsync(command, cancellationToken));

        Assert.Equal(before.Lines, (await schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(Repeats))]
    public async Task A_create_whose_base_and_caller_key_name_a_command_creates_none_and_finds_it_compared_as_a_repeat(
        string name,
        string mode,
        string script,
        string parameters,
        bool same)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, $"create_repeat_{name}", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow first = CreateFirst(agent, clock);
        Assert.Equal(new CommandMatch(first.Id, true, true), await store.CreateCommandAsync(first, cancellationToken));
        CommandRow repeat = Create(agent, "accounting", ModeOf(mode), CallerKey, clock.GetUtcNow(), script, parameters);

        CommandMatch? match = await store.CreateCommandAsync(repeat, cancellationToken);

        Assert.Equal(new CommandMatch(first.Id, false, same), match);
        Assert.Equal<Guid>([first.Id], (await schema.ListCommandsAsync(cancellationToken)).Select(row => row.Id));
    }

    // Concept rule 4: nothing ran, so the key is free again. The command ends as Expired when its deadline passed
    // before any poll took it, as Refused by its agent, or as Refused when its agent was revoked; another agent then
    // holds the base, and the new command is its own.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("expired")]
    [InlineData("refused_by_its_agent")]
    [InlineData("refused_at_revocation")]
    public async Task A_command_that_ended_as_Expired_or_Refused_frees_its_caller_key_for_a_new_command(string end)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, $"key_freed_{end}", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow first = Create(agent, "accounting", OutpostModeEnum.Write, CallerKey, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(first, cancellationToken));
        StoreAgent holder = agent;

        switch (end)
        {
            case "expired":
                clock.Advance(TakeTimeout);
                Assert.Equal(
                    OutpostOutcomeKindEnum.Expired,
                    (await store.PassCommandAsync(first.Id, cancellationToken)).Outcome?.Kind);
                break;

            case "refused_by_its_agent":
                await agent.PollAndTakeAsync([Serving("accounting", 1, 1)], [], cancellationToken);
                Assert.True(await agent.SendOutcomeAsync(
                    first.Id,
                    AgentOutcomes.Refused(OutpostOutcomeCodes.NotServed),
                    cancellationToken));
                break;

            default:
                Assert.True((await store.RevokeAgentAsync(agent.Id, cancellationToken)).Revoked);
                holder = await StoreAgent.PairAsync(store, clock, cancellationToken);
                await holder.RecordAsync(holder.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
                break;
        }

        Assert.Null(await store.FindCallerKeyAsync(
            "accounting",
            CallerKey,
            OutpostModeEnum.Write,
            Script,
            "{}",
            cancellationToken));

        CommandRow next = Create(holder, "accounting", OutpostModeEnum.Write, CallerKey, clock.GetUtcNow());

        Assert.Equal(new CommandMatch(next.Id, true, true), await store.CreateCommandAsync(next, cancellationToken));
        Assert.Equal<Guid>(
            [first.Id, next.Id],
            (await schema.ListCommandsAsync(cancellationToken)).Select(row => row.Id));
    }

    // Concept rules 4 and 5: a command that may have run keeps its key, so that a repeat never runs it again. It ended
    // as Succeeded by its agent, or as Unknown when its agent stayed away for its run limit after its lease; that agent
    // then comes back with a poll that lists it, so that a repeat could be created for it.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("succeeded")]
    [InlineData("unknown_disconnected")]
    public async Task A_command_that_ended_otherwise_keeps_its_caller_key(string end)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, $"key_kept_{end}", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow first = Create(agent, "accounting", OutpostModeEnum.Write, CallerKey, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(first, cancellationToken));
        await agent.PollAndTakeAsync([Serving("accounting", 1, 1)], [], cancellationToken);

        if (end == "succeeded")
        {
            Assert.True(await agent.SendOutcomeAsync(first.Id, AgentOutcomes.Succeeded("1"), cancellationToken));
        }
        else
        {
            clock.Advance(StoreAgent.Hold + StoreAgent.LeaseGrace + WriteRunLimit);
            Assert.Equal(
                OutpostOutcomeKindEnum.Unknown,
                (await store.PassCommandAsync(first.Id, cancellationToken)).Outcome?.Kind);
            await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], [first.Id]), cancellationToken);
        }

        CommandMatch? found = await store.FindCallerKeyAsync(
            "accounting",
            CallerKey,
            OutpostModeEnum.Write,
            Script,
            "{}",
            cancellationToken);
        CommandRow repeat = Create(agent, "accounting", OutpostModeEnum.Write, CallerKey, clock.GetUtcNow());

        Assert.Equal(new CommandMatch(first.Id, false, true), found);
        Assert.Equal(new CommandMatch(first.Id, false, true), await store.CreateCommandAsync(repeat, cancellationToken));
        Assert.Equal<Guid>([first.Id], (await schema.ListCommandsAsync(cancellationToken)).Select(row => row.Id));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Reads_without_a_caller_key_are_each_created()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "reads_without_key", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, null)], []), cancellationToken);
        CommandRow first = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        CommandRow second = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());

        Assert.Equal(new CommandMatch(first.Id, true, true), await store.CreateCommandAsync(first, cancellationToken));
        Assert.Equal(new CommandMatch(second.Id, true, true), await store.CreateCommandAsync(second, cancellationToken));
        Assert.Equal<Guid>(
            [first.Id, second.Id],
            (await schema.ListCommandsAsync(cancellationToken)).Select(row => row.Id));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task FindCallerKeyAsync_gives_null_when_no_command_holds_the_caller_key_on_the_base()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "caller_key_not_held", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(
            agent.NextPoll([Serving("accounting", 1, 1), Serving("warehouse", 1, 1)], []),
            cancellationToken);
        CommandRow command = Create(agent, "accounting", OutpostModeEnum.Write, CallerKey, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(command, cancellationToken));

        Assert.Null(await store.FindCallerKeyAsync(
            "warehouse",
            CallerKey,
            OutpostModeEnum.Write,
            Script,
            "{}",
            cancellationToken));
        Assert.Null(await store.FindCallerKeyAsync(
            "accounting",
            "invoice-43",
            OutpostModeEnum.Write,
            Script,
            "{}",
            cancellationToken));
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(Repeats))]
    public async Task FindCallerKeyAsync_gives_the_command_that_holds_the_caller_key_compared_as_a_repeat(
        string name,
        string mode,
        string script,
        string parameters,
        bool same)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, $"find_repeat_{name}", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow first = CreateFirst(agent, clock);
        Assert.NotNull(await store.CreateCommandAsync(first, cancellationToken));

        CommandMatch? match = await store.FindCallerKeyAsync(
            "accounting",
            CallerKey,
            ModeOf(mode),
            script,
            parameters,
            cancellationToken);

        Assert.Equal(new CommandMatch(first.Id, false, same), match);
    }

    // Concept rule 7: a command that no poll took ends at its take deadline, judged by the clock of the node.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task FindCallerKeyAsync_makes_a_waiting_command_Expired_from_its_deadline_on_and_then_finds_none()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "find_expires_waiting", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow command = Create(agent, "accounting", OutpostModeEnum.Write, CallerKey, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(command, cancellationToken));
        clock.Advance(TakeTimeout - TimeSpan.FromSeconds(1));

        CommandMatch? beforeDeadline = await store.FindCallerKeyAsync(
            "accounting",
            CallerKey,
            OutpostModeEnum.Write,
            Script,
            "{}",
            cancellationToken);

        Assert.Equal(new CommandMatch(command.Id, false, true), beforeDeadline);
        Assert.Null((await schema.FindCommandAsync(command.Id, cancellationToken))?.OutcomeKind);

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Null(await store.FindCallerKeyAsync(
            "accounting",
            CallerKey,
            OutpostModeEnum.Write,
            Script,
            "{}",
            cancellationToken));

        CommandRow? row = await schema.FindCommandAsync(command.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(CommandPhaseEnum.Waiting, row.Phase);
        Assert.Equal(OutpostOutcomeKindEnum.Expired, row.OutcomeKind);
        Assert.Null(row.OutcomeCode);
        Assert.Null(row.Outcome);
        Assert.Equal(clock.GetUtcNow(), row.EndedAt);
    }

    // Concept rule 7: a command that was handed out may still reach its agent, so it expires only on the evidence of
    // the agent's time, never by the node's clock.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task FindCallerKeyAsync_leaves_a_handed_out_command_whose_deadline_has_passed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "find_leaves_handed_out", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow command = Create(agent, "accounting", OutpostModeEnum.Write, CallerKey, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(command, cancellationToken));
        await agent.PollAndTakeAsync([Serving("accounting", 1, 1)], [], cancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));

        CommandMatch? match = await store.FindCallerKeyAsync(
            "accounting",
            CallerKey,
            OutpostModeEnum.Write,
            Script,
            "{}",
            cancellationToken);

        Assert.Equal(new CommandMatch(command.Id, false, true), match);

        CommandRow? row = await schema.FindCommandAsync(command.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(CommandPhaseEnum.HandedOut, row.Phase);
        Assert.Null(row.OutcomeKind);
    }

    // The race of two first calls with one key, driven to a known order as the race of two claims of a key is:
    // another connection holds an uncommitted command with the base and the caller key, as a create that has not
    // committed yet, until both creates are seen waiting for it, and then rolls it back. Which create makes the command
    // is not the subject; that one does and the other finds it, the same, is.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Of_two_first_creates_with_one_base_and_caller_key_one_creates_the_command_and_the_other_finds_it()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "first_creates_at_once", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);

        Guid created = await RaceTwoCreatesAsync(schema, store, agent, clock, cancellationToken);

        Assert.Equal<Guid>([created], (await schema.ListCommandsAsync(cancellationToken)).Select(row => row.Id));
    }

    // Concept rule 4: the race of two repeats after the first command freed the key, driven to a known order as the
    // race of two first creates is. The first command ended as Expired when its deadline passed before any poll took
    // it, or as Refused by its agent.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("expired")]
    [InlineData("refused")]
    public async Task Of_two_repeats_that_create_after_the_first_command_ended_as_Expired_or_Refused_one_creates_the_command(
        string end)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, $"repeats_at_once_{end}", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow ended = Create(agent, "accounting", OutpostModeEnum.Write, CallerKey, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(ended, cancellationToken));

        if (end == "expired")
        {
            clock.Advance(TakeTimeout);
            Assert.Equal(
                OutpostOutcomeKindEnum.Expired,
                (await store.PassCommandAsync(ended.Id, cancellationToken)).Outcome?.Kind);
        }
        else
        {
            await agent.PollAndTakeAsync([Serving("accounting", 1, 1)], [], cancellationToken);
            Assert.True(await agent.SendOutcomeAsync(
                ended.Id,
                AgentOutcomes.Refused(OutpostOutcomeCodes.NotServed),
                cancellationToken));
        }

        Guid created = await RaceTwoCreatesAsync(schema, store, agent, clock, cancellationToken);

        Assert.Equal<Guid>(
            [ended.Id, created],
            (await schema.ListCommandsAsync(cancellationToken)).Select(row => row.Id));
    }

    // Two creates of commands with one base and caller key, each with an identity of its own, held at the key as the
    // races above say; asserts that one created its command and the other found that one, the same, and gives its
    // identity.
    private static async Task<Guid> RaceTwoCreatesAsync(
        MigratedSchema schema,
        OutpostStore store,
        StoreAgent agent,
        FakeClock clock,
        CancellationToken cancellationToken)
    {
        CommandRow first = Create(agent, "accounting", OutpostModeEnum.Write, CallerKey, clock.GetUtcNow());
        CommandRow second = Create(agent, "accounting", OutpostModeEnum.Write, CallerKey, clock.GetUtcNow());

        await using HeldLock keyLock = await HeldLock.TakeAsync(
            schema.ConnectionString,
            $"""
            INSERT INTO {schema.Name}.commands
                (id, agent_id, base_key, mode, caller_key, bytes, signature, deadline, skew, outcome_limit, phase)
            VALUES
                ('{Guid.NewGuid()}', '{agent.Id}', 'accounting', {(int)OutpostModeEnum.Write}, '{CallerKey}',
                 decode('00', 'hex'), decode('00', 'hex'), '{first.Deadline:O}', interval '2 minutes', {OutcomeLimit},
                 {(int)CommandPhaseEnum.Waiting})
            """,
            cancellationToken);

        Task<CommandMatch?> firstCreate = store.CreateCommandAsync(first, cancellationToken);
        Task<CommandMatch?> secondCreate = store.CreateCommandAsync(second, cancellationToken);

        await keyLock.WaitForAsync([firstCreate, secondCreate], cancellationToken);
        await keyLock.ReleaseAsync(cancellationToken);

        CommandMatch? firstMatch = await firstCreate;
        CommandMatch? secondMatch = await secondCreate;

        Assert.NotNull(firstMatch);
        Assert.NotNull(secondMatch);
        Assert.NotEqual(firstMatch.Created, secondMatch.Created);

        Guid created = firstMatch.Created ? first.Id : second.Id;

        Assert.Equal(new CommandMatch(created, true, true), firstMatch.Created ? firstMatch : secondMatch);
        Assert.Equal(new CommandMatch(created, false, true), firstMatch.Created ? secondMatch : firstMatch);

        return created;
    }

    // The first command of the repeats: a Write with the script and the parameters above.
    private static CommandRow CreateFirst(StoreAgent agent, FakeClock clock)
    {
        return Create(
            agent,
            "accounting",
            OutpostModeEnum.Write,
            CallerKey,
            clock.GetUtcNow(),
            FirstScript,
            FirstParameters);
    }

    private static OutpostModeEnum ModeOf(string mode)
    {
        return mode == "write" ? OutpostModeEnum.Write : OutpostModeEnum.Read;
    }
}
