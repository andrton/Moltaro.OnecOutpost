using System.Buffers.Text;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Store;
using static Moltaro.OnecOutpost.Server.Tests.Store.StoreCommands;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// The take of a poll in the store against a real PostgreSQL, with the fake clock (concept rules 7, 8, 10, 11, 13 and
// 14; §6.2 and §10 of docs/protocol.md). Only the newest poll of a Paired agent takes. For each base and mode that it
// declares with free workers, it takes at most `free` of its agent's commands with no outcome, oldest first by the
// store's sequence: those that no poll took, and those that an older poll handed out and no newest poll listed as
// received, while their deadline is later than the node's now and their key is one the agent holds. It keeps them in
// that order while their entries fit the room of the answer, the first always, and makes them HandedOut with the run
// limit of their mode. Each test works in a schema of its own.
[Trait("Requires", "PostgreSQL")]
public sealed class TakeTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task A_take_hands_out_a_command_as_it_was_signed_and_makes_it_HandedOut_with_the_run_limit_of_its_mode()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "take_hands_out", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        OutpostPollRequest poll = agent.NextPoll([Serving("accounting", 1, 1)], []);
        await agent.RecordAsync(poll, cancellationToken);
        CommandRow write = Create(agent, "accounting", OutpostModeEnum.Write, "invoice-42", clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(write, cancellationToken));

        PollTake take = await store.TakeCommandsAsync(agent.Id, poll, Room, cancellationToken);

        Assert.False(take.State.Revoked);
        Assert.Null(take.State.Newest);
        Assert.Equal<string>(["accounting"], take.State.Held);
        Assert.Empty(take.State.Claimed);

        OutpostSignedCommand handed = Assert.Single(take.Commands);
        Assert.Equal(write.Bytes, handed.Bytes);
        Assert.Equal(write.Signature, handed.Signature);

        CommandRow? row = await schema.FindCommandAsync(write.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(CommandPhaseEnum.HandedOut, row.Phase);
        Assert.Equal(WriteRunLimit, row.RunLimit);
        Assert.Null(row.OutcomeKind);
        Assert.Null(row.EndedAt);
    }

    // Concept rule 8. The poll has two free workers for the Reads of one base and one for its Writes, one for the
    // Reads of a second base, and none for the Reads of a third. The identities of the commands sort against the order
    // of their creation, so that only the sequence gives the order.
    [Fact]
    public async Task A_take_gives_at_most_free_commands_of_each_base_and_mode_of_its_poll_oldest_first()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "take_free_oldest_first", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        OutpostPollRequest poll = agent.NextPoll(
            [Serving("accounting", 2, 1), Serving("warehouse", 1, null), Serving("sales", 0, 1)],
            []);
        await agent.RecordAsync(poll, cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        CommandRow accountingRead1 = Create(agent, "accounting", OutpostModeEnum.Read, null, now, id: Descending(8));
        CommandRow warehouseRead1 = Create(agent, "warehouse", OutpostModeEnum.Read, null, now, id: Descending(7));
        CommandRow accountingWrite1 =
            Create(agent, "accounting", OutpostModeEnum.Write, "invoice-1", now, id: Descending(6));
        CommandRow accountingRead2 = Create(agent, "accounting", OutpostModeEnum.Read, null, now, id: Descending(5));
        CommandRow accountingRead3 = Create(agent, "accounting", OutpostModeEnum.Read, null, now, id: Descending(4));
        CommandRow accountingWrite2 =
            Create(agent, "accounting", OutpostModeEnum.Write, "invoice-2", now, id: Descending(3));
        CommandRow warehouseRead2 = Create(agent, "warehouse", OutpostModeEnum.Read, null, now, id: Descending(2));
        CommandRow salesRead1 = Create(agent, "sales", OutpostModeEnum.Read, null, now, id: Descending(1));

        foreach (CommandRow command in (CommandRow[])[
            accountingRead1,
            warehouseRead1,
            accountingWrite1,
            accountingRead2,
            accountingRead3,
            accountingWrite2,
            warehouseRead2,
            salesRead1])
        {
            Assert.NotNull(await store.CreateCommandAsync(command, cancellationToken));
        }

        PollTake take = await store.TakeCommandsAsync(agent.Id, poll, Room, cancellationToken);

        Guid[] taken = [accountingRead1.Id, warehouseRead1.Id, accountingWrite1.Id, accountingRead2.Id];

        Assert.Equal<Guid>(taken, Identities(take));

        IReadOnlyList<CommandRow> rows = await schema.ListCommandsAsync(cancellationToken);
        Assert.Equal<Guid>(taken, rows.Where(row => row.Phase == CommandPhaseEnum.HandedOut).Select(row => row.Id));
        Assert.Equal<Guid>(
            [accountingRead3.Id, accountingWrite2.Id, warehouseRead2.Id, salesRead1.Id],
            rows.Where(row => row.Phase == CommandPhaseEnum.Waiting).Select(row => row.Id));
    }

    // Concept rule 11 and §6.2 of docs/protocol.md: the answer to the older poll was lost, so the newer poll does not
    // list the command as received. It is handed out again like any other command, with the run limit that the newer
    // poll declares for Reads.
    [Fact]
    public async Task A_command_that_the_newest_poll_does_not_list_is_handed_out_again_against_free_with_the_run_limit_of_that_poll()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "take_hands_out_again", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, null)], []), cancellationToken);
        CommandRow first = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        CommandRow second = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(first, cancellationToken));
        Assert.NotNull(await store.CreateCommandAsync(second, cancellationToken));
        Assert.Equal<Guid>(
            [first.Id],
            Identities(await agent.PollAndTakeAsync([Serving("accounting", 1, null)], [], cancellationToken)));
        clock.Advance(TimeSpan.FromSeconds(10));
        OutpostPollBase newerRunLimit = new("accounting", new OutpostPollMode(1, TimeSpan.FromMinutes(9)), null);

        PollTake again = await agent.PollAndTakeAsync([newerRunLimit], [], cancellationToken);

        Assert.Equal<Guid>([first.Id], Identities(again));

        CommandRow? row = await schema.FindCommandAsync(first.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(CommandPhaseEnum.HandedOut, row.Phase);
        Assert.Equal(TimeSpan.FromMinutes(9), row.RunLimit);
        Assert.Equal(CommandPhaseEnum.Waiting, (await schema.FindCommandAsync(second.Id, cancellationToken))?.Phase);
    }

    // Concept rules 3 and 11: a command that its agent listed as received reached it, and is never handed out again.
    [Fact]
    public async Task A_command_that_the_newest_poll_lists_as_received_is_never_handed_out_again()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "take_not_received_again", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, null)], []), cancellationToken);
        CommandRow first = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        CommandRow second = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(first, cancellationToken));
        Assert.NotNull(await store.CreateCommandAsync(second, cancellationToken));
        await agent.PollAndTakeAsync([Serving("accounting", 1, null)], [], cancellationToken);

        PollTake next = await agent.PollAndTakeAsync([Serving("accounting", 1, null)], [first.Id], cancellationToken);

        Assert.Equal<Guid>([second.Id], Identities(next));
        Assert.Equal(CommandPhaseEnum.Received, (await schema.FindCommandAsync(first.Id, cancellationToken))?.Phase);
    }

    // Concept rules 7 and 11: neither a command that no poll took nor one handed out again is handed out from its
    // deadline on, by the node's clock. The poll that lists neither is signed before the deadline plus the skew, so the
    // command that was handed out stays HandedOut.
    [Fact]
    public async Task A_take_hands_out_no_command_from_its_deadline_on()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "take_after_deadline", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, null)], []), cancellationToken);
        CommandRow handedOut = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        CommandRow waiting = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(handedOut, cancellationToken));
        Assert.NotNull(await store.CreateCommandAsync(waiting, cancellationToken));
        await agent.PollAndTakeAsync([Serving("accounting", 1, null)], [], cancellationToken);
        clock.Advance(TakeTimeout);

        PollTake take = await agent.PollAndTakeAsync([Serving("accounting", 2, null)], [], cancellationToken);

        Assert.Empty(take.Commands);

        CommandRow? handedOutRow = await schema.FindCommandAsync(handedOut.Id, cancellationToken);
        CommandRow? waitingRow = await schema.FindCommandAsync(waiting.Id, cancellationToken);
        Assert.NotNull(handedOutRow);
        Assert.NotNull(waitingRow);
        Assert.Equal(CommandPhaseEnum.HandedOut, handedOutRow.Phase);
        Assert.Null(handedOutRow.OutcomeKind);
        Assert.Equal(CommandPhaseEnum.Waiting, waitingRow.Phase);
        Assert.Null(waitingRow.OutcomeKind);
    }

    // Concept rule 11: an outcome keeps a command from the take, the agent's, and one the server set, which waits for
    // the agent's outcome or for the poll that makes it Expired. The command's take timeout is long enough for the run
    // limit of one minute to end it before its deadline, and the poll that follows lists it no more and is signed
    // before that deadline.
    [Theory]
    [InlineData("of_its_agent")]
    [InlineData("set_by_the_server")]
    public async Task A_take_hands_out_no_command_that_has_an_outcome(string outcome)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, $"take_outcome_{outcome}", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        OutpostPollBase shortRun = new("accounting", new OutpostPollMode(1, TimeSpan.FromMinutes(1)), null);
        await agent.RecordAsync(agent.NextPoll([shortRun], []), cancellationToken);
        CommandRow read = Create(
            agent,
            "accounting",
            OutpostModeEnum.Read,
            null,
            clock.GetUtcNow(),
            takeTimeout: TimeSpan.FromMinutes(10));
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));
        await agent.PollAndTakeAsync([shortRun], [], cancellationToken);

        if (outcome == "of_its_agent")
        {
            Assert.True(await agent.SendOutcomeAsync(read.Id, AgentOutcomes.Succeeded("1"), cancellationToken));
        }
        else
        {
            clock.Advance(StoreAgent.Hold + StoreAgent.LeaseGrace + TimeSpan.FromMinutes(1));
            Assert.Equal(
                OutpostOutcomeKindEnum.Failed,
                (await store.PassCommandAsync(read.Id, cancellationToken))!.Outcome?.Kind);
        }

        PollTake take = await agent.PollAndTakeAsync([shortRun], [], cancellationToken);

        Assert.Empty(take.Commands);
        Assert.NotNull((await schema.FindCommandAsync(read.Id, cancellationToken))?.OutcomeKind);
    }

    // The newer poll closes Write on the first base and no longer declares the second.
    [Fact]
    public async Task A_take_hands_out_no_command_for_a_mode_or_a_base_that_its_poll_does_not_declare()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "take_mode_not_declared", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(
            agent.NextPoll([Serving("accounting", 1, 1), Serving("warehouse", 1, 1)], []),
            cancellationToken);
        CommandRow write = Create(agent, "accounting", OutpostModeEnum.Write, "invoice-42", clock.GetUtcNow());
        CommandRow read = Create(agent, "warehouse", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(write, cancellationToken));
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));

        PollTake take = await agent.PollAndTakeAsync([Serving("accounting", 1, null)], [], cancellationToken);

        Assert.Empty(take.Commands);
        Assert.Equal(CommandPhaseEnum.Waiting, (await schema.FindCommandAsync(write.Id, cancellationToken))?.Phase);
        Assert.Equal(CommandPhaseEnum.Waiting, (await schema.FindCommandAsync(read.Id, cancellationToken))?.Phase);
    }

    // Concept rule 14: the command was made while its agent held the key. The application released the key after the
    // agent stopped declaring it, another agent claimed it, and the first agent declares it again, claimed for it.
    [Fact]
    public async Task A_command_made_before_its_key_was_released_is_handed_out_neither_to_its_agent_nor_to_the_next_holder()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "take_key_released", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent first = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent next = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await first.RecordAsync(first.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow read = Create(first, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));
        await first.RecordAsync(first.NextPoll([], []), cancellationToken);
        Assert.Equal(OutpostReleaseResultEnum.Released, await store.ReleaseBaseAsync("accounting", cancellationToken));

        PollTake nextTake = await next.PollAndTakeAsync([Serving("accounting", 1, 1)], [], cancellationToken);
        PollTake firstTake = await first.PollAndTakeAsync([Serving("accounting", 1, 1)], [], cancellationToken);

        Assert.Empty(nextTake.Commands);
        Assert.Equal<string>(["accounting"], nextTake.State.Held);
        Assert.Empty(firstTake.Commands);
        Assert.Equal<string>(["accounting"], firstTake.State.Claimed);
        Assert.Equal(CommandPhaseEnum.Waiting, (await schema.FindCommandAsync(read.Id, cancellationToken))?.Phase);
    }

    // Concept rule 10.
    [Fact]
    public async Task A_take_of_a_poll_that_is_no_longer_the_newest_takes_nothing_and_its_state_names_the_newest_number()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "take_of_older_poll", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        OutpostPollRequest older = agent.NextPoll([Serving("accounting", 1, null)], []);
        await agent.RecordAsync(older, cancellationToken);
        CommandRow read = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 0, null)], []), cancellationToken);

        PollTake take = await store.TakeCommandsAsync(agent.Id, older, Room, cancellationToken);

        Assert.Empty(take.Commands);
        Assert.False(take.State.Revoked);
        Assert.Equal(agent.Number, take.State.Newest);
        Assert.Equal(CommandPhaseEnum.Waiting, (await schema.FindCommandAsync(read.Id, cancellationToken))?.Phase);
    }

    // Concept rule 13.
    [Fact]
    public async Task A_take_of_a_revoked_agent_takes_nothing_and_its_state_says_revoked()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "take_of_revoked", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        OutpostPollRequest poll = agent.NextPoll([Serving("accounting", 1, null)], []);
        await agent.RecordAsync(poll, cancellationToken);
        CommandRow read = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));
        Assert.True((await store.RevokeAgentAsync(agent.Id, cancellationToken)).Revoked);

        PollTake take = await store.TakeCommandsAsync(agent.Id, poll, Room, cancellationToken);

        Assert.Empty(take.Commands);
        Assert.True(take.State.Revoked);
    }

    // §10 of docs/protocol.md: an answer is filled only up to its limit, and always holds one command when one is due.
    // The scripts make each entry of the answer about 4 500 bytes, so that a room of two and a half entries holds two
    // of them whatever the few bytes around each entry.
    [Theory]
    [InlineData(0.25, 1)]
    [InlineData(2.5, 2)]
    [InlineData(3.5, 3)]
    public async Task A_take_keeps_the_oldest_commands_while_their_entries_fit_the_room_and_always_the_first(
        double entries,
        int taken)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, $"take_room_{taken}", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        OutpostPollRequest poll = agent.NextPoll([Serving("accounting", 3, null)], []);
        await agent.RecordAsync(poll, cancellationToken);
        CommandRow[] commands = [Large(agent, clock), Large(agent, clock), Large(agent, clock)];

        foreach (CommandRow command in commands)
        {
            Assert.NotNull(await store.CreateCommandAsync(command, cancellationToken));
        }

        long room = (long)(entries * EntryLength(commands[0]));

        PollTake take = await store.TakeCommandsAsync(agent.Id, poll, room, cancellationToken);

        Assert.Equal<Guid>(commands.Take(taken).Select(command => command.Id), Identities(take));
    }

    // The second command is too large for the room that the first and the third fit: commands are handed out oldest
    // first, so the third waits for an answer of its own.
    [Fact]
    public async Task A_take_hands_out_no_command_after_the_first_one_that_does_not_fit_the_room()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "take_room_in_order", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        OutpostPollRequest poll = agent.NextPoll([Serving("accounting", 3, null)], []);
        await agent.RecordAsync(poll, cancellationToken);
        CommandRow first = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        CommandRow large = Large(agent, clock);
        CommandRow third = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());

        foreach (CommandRow command in (CommandRow[])[first, large, third])
        {
            Assert.NotNull(await store.CreateCommandAsync(command, cancellationToken));
        }

        long room = EntryLength(first) + EntryLength(third) + 1_000;
        Assert.True(room < EntryLength(first) + EntryLength(large));

        PollTake take = await store.TakeCommandsAsync(agent.Id, poll, room, cancellationToken);

        Assert.Equal<Guid>([first.Id], Identities(take));
    }

    // The race of two takes of one command. Two takes of one agent wait for each other on its row, so the race is a
    // take that finds the row of the command held by another transaction, as by a take that has not committed yet. It
    // skips the row without waiting and changes nothing; once the row is free, the same poll takes the command.
    [Fact]
    public async Task A_take_that_finds_the_row_of_a_command_held_by_another_take_skips_it_without_waiting()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "takes_at_once", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        OutpostPollRequest poll = agent.NextPoll([Serving("accounting", 1, null)], []);
        await agent.RecordAsync(poll, cancellationToken);
        CommandRow read = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));

        await using (HeldLock rowLock = await HeldLock.TakeAsync(
            schema.ConnectionString,
            $"SELECT id FROM {schema.Name}.commands WHERE id = '{read.Id}' FOR UPDATE",
            cancellationToken))
        {
            PollTake skipped = await rowLock.WithoutWaitingAsync(
                store.TakeCommandsAsync(agent.Id, poll, Room, cancellationToken),
                cancellationToken);

            Assert.Empty(skipped.Commands);

            await rowLock.ReleaseAsync(cancellationToken);
        }

        CommandRow? row = await schema.FindCommandAsync(read.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(CommandPhaseEnum.Waiting, row.Phase);
        Assert.Null(row.RunLimit);

        PollTake take = await store.TakeCommandsAsync(agent.Id, poll, Room, cancellationToken);

        Assert.Equal<Guid>([read.Id], Identities(take));
    }

    // Concept rule 7: taking and expiring are one conditional change of the command, each judged by the clock of the
    // node that makes it, and the first to commit wins. The expiry by a node whose clock reached the deadline holds its
    // change open; the take on a node whose clock has not skips the command without waiting, and the expiry commits.
    [Fact]
    public async Task A_take_that_finds_a_command_held_by_its_expiry_skips_it_and_the_command_ends_as_Expired()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "take_against_expiry", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        OutpostPollRequest poll = agent.NextPoll([Serving("accounting", 1, null)], []);
        await agent.RecordAsync(poll, cancellationToken);
        CommandRow read = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));

        await using (HeldLock expiry = await HeldLock.TakeAsync(
            schema.ConnectionString,
            $"""
            UPDATE {schema.Name}.commands
            SET outcome_kind = {(int)OutpostOutcomeKindEnum.Expired}, ended_at = '{read.Deadline:O}'
            WHERE id = '{read.Id}'
            """,
            cancellationToken))
        {
            PollTake skipped = await expiry.WithoutWaitingAsync(
                store.TakeCommandsAsync(agent.Id, poll, Room, cancellationToken),
                cancellationToken);

            Assert.Empty(skipped.Commands);

            await expiry.CommitAsync(cancellationToken);
        }

        CommandRow? row = await schema.FindCommandAsync(read.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(CommandPhaseEnum.Waiting, row.Phase);
        Assert.Null(row.RunLimit);
        Assert.Equal(OutpostOutcomeKindEnum.Expired, row.OutcomeKind);
        Assert.Empty((await store.TakeCommandsAsync(agent.Id, poll, Room, cancellationToken)).Commands);
    }

    // Concept rule 7, the other order: the take on a node whose clock has not reached the deadline holds its change
    // open, and the pass of a call on a node whose clock has waits for the command and then finds it taken. It ends
    // nothing, and gives the end of the agent's lease plus the run limit of the take as the moment time can end it.
    [Fact]
    public async Task An_expiry_that_finds_a_command_held_by_its_take_waits_for_it_and_then_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "expiry_against_take", cancellationToken);
        FakeClock clock = new();
        FakeClock laterClock = new();
        laterClock.Advance(TakeTimeout);
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        OutpostStore laterNode = new(schema.ConnectionString, schema.Name, laterClock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, null)], []), cancellationToken);
        CommandRow read = Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow());
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));

        Task<CommandPass?> pass;

        await using (HeldLock take = await HeldLock.TakeAsync(
            schema.ConnectionString,
            $"""
            UPDATE {schema.Name}.commands
            SET phase = {(int)CommandPhaseEnum.HandedOut}, run_limit = interval '{ReadRunLimit.TotalSeconds} seconds'
            WHERE id = '{read.Id}'
            """,
            cancellationToken))
        {
            pass = laterNode.PassCommandAsync(read.Id, cancellationToken);

            await take.WaitForAsync([pass], cancellationToken);
            await take.CommitAsync(cancellationToken);
        }

        CommandPass passed = (await pass)!;
        AgentRow? agentRow = await schema.FindAgentAsync(agent.Id, cancellationToken);

        Assert.Null(passed.Outcome);
        Assert.NotNull(agentRow?.LeaseEndsAt);
        Assert.Equal(agentRow.LeaseEndsAt.Value + ReadRunLimit, passed.Due);

        CommandRow? row = await schema.FindCommandAsync(read.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(CommandPhaseEnum.HandedOut, row.Phase);
        Assert.Null(row.OutcomeKind);
    }

    // An identity that sorts below the identity of every greater rank.
    private static Guid Descending(int rank)
    {
        return Guid.Parse($"{rank:D8}-0000-4000-8000-000000000000");
    }

    // A Read whose script is about 3 000 bytes.
    private static CommandRow Large(StoreAgent agent, FakeClock clock)
    {
        string script = $"Result = \"{new string('x', 3_000)}\";";

        return Create(agent, "accounting", OutpostModeEnum.Read, null, clock.GetUtcNow(), script);
    }

    // The bytes of a command and its signature in base64url, as the entry of a poll answer carries them (§6.2), without
    // the few bytes of the entry's own JSON.
    private static long EntryLength(CommandRow command)
    {
        return Base64Url.EncodeToString(command.Bytes).Length + Base64Url.EncodeToString(command.Signature).Length;
    }
}
