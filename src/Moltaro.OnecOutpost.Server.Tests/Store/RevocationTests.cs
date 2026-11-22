using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Store;
using static Moltaro.OnecOutpost.Server.Tests.Store.StoreCommands;
using static Moltaro.OnecOutpost.TestSupport.Loopback.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// Revoking an agent in the store against a real PostgreSQL (concept rule 13): one short transaction deletes its token,
// makes it Revoked, frees the keys it held (rule 14) and ends its commands that have no outcome. The bases of its
// newest poll stay, so that the list of agents still shows them, none held. Each test works in a schema of its own.
[Trait("Requires", "PostgreSQL")]
public sealed class RevocationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task Revoking_a_paired_agent_gives_true_and_frees_its_keys_for_the_next_agent_that_declares_them()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "revoke_paired", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent revoked = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent next = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await revoked.PollAsync([ReadAndWrite("accounting")], cancellationToken);

        Assert.True((await store.RevokeAgentAsync(revoked.Id, cancellationToken)).Revoked);

        AgentRow? row = await schema.FindAgentAsync(revoked.Id, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(OutpostAgentStateEnum.Revoked, row.State);
        Assert.Null(await schema.FindHolderAsync("accounting", cancellationToken));
        Assert.Equal<(string, bool)>(
            [("accounting", true)],
            await schema.FindBasesAsync(revoked.Id, cancellationToken));

        PollState state = await next.PollAsync([ReadOnly("accounting")], cancellationToken);

        Assert.Equal<string>(["accounting"], state.Held);
    }

    [Fact]
    public async Task Revoking_a_revoked_agent_or_an_identity_the_store_does_not_know_gives_false_and_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "revoke_again", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.PollAsync([ReadOnly("accounting")], cancellationToken);
        Assert.True((await store.RevokeAgentAsync(agent.Id, cancellationToken)).Revoked);
        SchemaSnapshot before = await schema.TakeSnapshotAsync(cancellationToken);

        Assert.False((await store.RevokeAgentAsync(agent.Id, cancellationToken)).Revoked);
        Assert.False((await store.RevokeAgentAsync(Guid.NewGuid(), cancellationToken)).Revoked);

        Assert.Equal(before.Lines, (await schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }

    // Concept rules 3 and 13. Of the agent's taken commands, a Read was only handed out and a Write is in its list as
    // received; a third command has the agent's outcome, and another agent's command waits for a poll.
    [Fact]
    public async Task Revoking_an_agent_ends_its_commands_with_no_outcome_as_Refused_revoked_when_no_poll_took_them_and_as_Unknown_revoked_or_Failed_lost_when_taken()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "revoke_ends_commands", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent other = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 2, 1)], []), cancellationToken);
        await other.RecordAsync(other.NextPoll([Serving("warehouse", 0, null)], []), cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();
        CommandRow takenRead = Create(agent, "accounting", OutpostModeEnum.Read, null, now);
        CommandRow takenWrite = Create(agent, "accounting", OutpostModeEnum.Write, "invoice-1", now);
        CommandRow endedRead = Create(agent, "accounting", OutpostModeEnum.Read, null, now);
        CommandRow otherRead = Create(other, "warehouse", OutpostModeEnum.Read, null, now);

        foreach (CommandRow command in (CommandRow[])[takenRead, takenWrite, endedRead, otherRead])
        {
            Assert.NotNull(await store.CreateCommandAsync(command, cancellationToken));
        }

        await agent.PollAndTakeAsync([Serving("accounting", 2, 1)], [], cancellationToken);
        Assert.True(await agent.SendOutcomeAsync(endedRead.Id, AgentOutcomes.Succeeded("1"), cancellationToken));
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 0)], [takenWrite.Id]), cancellationToken);
        CommandRow waitingRead = Create(agent, "accounting", OutpostModeEnum.Read, null, now);
        CommandRow waitingWrite = Create(agent, "accounting", OutpostModeEnum.Write, "invoice-2", now);
        Assert.NotNull(await store.CreateCommandAsync(waitingRead, cancellationToken));
        Assert.NotNull(await store.CreateCommandAsync(waitingWrite, cancellationToken));
        clock.Advance(TimeSpan.FromSeconds(5));
        CommandRow? endedBefore = await schema.FindCommandAsync(endedRead.Id, cancellationToken);
        CommandRow? otherBefore = await schema.FindCommandAsync(otherRead.Id, cancellationToken);

        Revocation revocation = await store.RevokeAgentAsync(agent.Id, cancellationToken);

        (CommandRow Command, OutpostOutcomeKindEnum Kind, string Code)[] ends =
        [
            (takenRead, OutpostOutcomeKindEnum.Failed, OutpostCodes.Lost),
            (takenWrite, OutpostOutcomeKindEnum.Unknown, OutpostCodes.Revoked),
            (waitingRead, OutpostOutcomeKindEnum.Refused, OutpostCodes.Revoked),
            (waitingWrite, OutpostOutcomeKindEnum.Refused, OutpostCodes.Revoked),
        ];

        Assert.True(revocation.Revoked);
        Assert.Equal<Guid>(ends.Select(end => end.Command.Id).Order(), revocation.Ended.Order());

        foreach ((CommandRow command, OutpostOutcomeKindEnum kind, string code) in ends)
        {
            CommandRow? row = await schema.FindCommandAsync(command.Id, cancellationToken);
            Assert.NotNull(row);
            Assert.Equal(kind, row.OutcomeKind);
            Assert.Equal(code, row.OutcomeCode);
            Assert.Null(row.Outcome);
            Assert.Equal(clock.GetUtcNow(), row.EndedAt);
        }

        Assert.Equivalent(endedBefore, await schema.FindCommandAsync(endedRead.Id, cancellationToken), strict: true);
        Assert.Equivalent(otherBefore, await schema.FindCommandAsync(otherRead.Id, cancellationToken), strict: true);

        OutpostOutcome? refused = (await store.PassCommandAsync(waitingWrite.Id, cancellationToken)).Outcome;
        Assert.NotNull(refused);
        Assert.Equal(waitingWrite.Id, refused.CommandId);
        Assert.Equal(OutpostOutcomeKindEnum.Refused, refused.Kind);
        Assert.Equal(OutpostCodes.Revoked, refused.Code);
    }

    // Concept rules 6 and 13: the race of a create and a revocation, driven to a known order as the race of two claims
    // of a key is. Another connection makes the agent Revoked, as a revocation does before it ends the agent's
    // commands, and holds that change open until the create is seen waiting for it; then it commits. A create that read
    // the agent without waiting for it would make a command that the revocation, already past its commands, never ends.
    [Fact]
    public async Task A_create_that_meets_a_revocation_waits_for_it_and_then_creates_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "create_against_revoke", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.RecordAsync(agent.NextPoll([Serving("accounting", 1, 1)], []), cancellationToken);
        CommandRow command = Create(agent, "accounting", OutpostModeEnum.Write, "invoice-42", clock.GetUtcNow());
        Task<CommandMatch?> create;

        await using (HeldLock revocation = await HeldLock.TakeAsync(
            schema.ConnectionString,
            $"UPDATE {schema.Name}.agents SET state = {(int)OutpostAgentStateEnum.Revoked} WHERE id = '{agent.Id}'",
            cancellationToken))
        {
            create = store.CreateCommandAsync(command, cancellationToken);

            await revocation.WaitForAsync([create], cancellationToken);
            await revocation.CommitAsync(cancellationToken);
        }

        Assert.Null(await create);
        Assert.Empty(await schema.ListCommandsAsync(cancellationToken));
    }

    // The public contract: revoking a pending agent ends its token, which then pairs nothing.
    [Fact]
    public async Task Revoking_a_pending_agent_ends_its_token()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "revoke_pending", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        Guid agentId = Guid.NewGuid();
        byte[] secret = RandomNumberGenerator.GetBytes(16);
        DateTimeOffset expiresAt = clock.GetUtcNow() + TimeSpan.FromMinutes(15);
        await store.CreatePairingTokenAsync(agentId, "Test agent", secret, expiresAt, cancellationToken);

        Assert.True((await store.RevokeAgentAsync(agentId, cancellationToken)).Revoked);

        AgentRow? row = await schema.FindAgentAsync(agentId, cancellationToken);
        Assert.NotNull(row);
        Assert.Equal(OutpostAgentStateEnum.Revoked, row.State);
        Assert.Null(await schema.FindTokenAsync(agentId, cancellationToken));
        Assert.Null(await store.FindPairingSecretAsync(agentId, cancellationToken));
        Assert.False(await store.RedeemPairingTokenAsync(agentId, new byte[91], cancellationToken));
    }
}
