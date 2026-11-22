using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.Server.Tests.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;

namespace Moltaro.OnecOutpost.Server.Tests.Calls;

// Concept rule 11 over loopback HTTP, with the fake clock: a taken command whose agent stays away longer than its run
// limit ends as rule 3 says, Unknown `disconnected` for a Write and Failed `lost` for a Read; the outcome of the agent
// then replaces that outcome, and a poll that proves the command never reached the agent turns it into Expired. A
// command is due at the end of its agent's lease plus the run limit of its mode in the poll that handed it out, the
// limits of StoreCommands.Serving. The poll that hands the command out is the agent's last until the test says, so its
// lease ends at its hold plus the lease grace after it was recorded.
[Trait("Requires", "PostgreSQL")]
public sealed class RunLimitTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // The call waits PollTimeout at most at a time, so the clock moves to a microsecond before the command is due,
    // where the call passes it and waits again until it is due, and then to the moment it is due. A microsecond is the
    // step of the store's times.
    [Theory]
    [InlineData("read", OutpostOutcomeKindEnum.Failed, OutpostCodes.Lost)]
    [InlineData("write", OutpostOutcomeKindEnum.Unknown, OutpostCodes.Disconnected)]
    public async Task A_taken_command_whose_agent_stays_away_longer_than_its_run_limit_ends_as_rule_3_says(
        string mode,
        OutpostOutcomeKindEnum kind,
        string code)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"calls_run_limit_{mode}",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        DateTimeOffset start = server.Clock.GetUtcNow();
        (Task<OutpostOutcome> call, OutpostCommandMessage command) = await HandOutAsync(
            server,
            agent,
            mode,
            "key-17",
            cancellationToken);
        DateTimeOffset due = Due(server, start, mode);
        TimeSpan microsecond = TimeSpan.FromMicroseconds(1);

        await server.WaitForTimerAsync(start + server.Options.PollTimeout, cancellationToken);
        server.Clock.Advance(due - microsecond - start);
        await server.WaitForTimerAsync(due, cancellationToken);
        Assert.Null((await server.Schema.FindCommandAsync(command.Id, cancellationToken))?.OutcomeKind);
        server.Clock.Advance(microsecond);

        OutpostOutcome outcome = await call;

        Assert.Equal(kind, outcome.Kind);
        Assert.Equal(code, outcome.Code);
        Assert.Equal(command.Id, outcome.CommandId);
    }

    // The agent comes back with the outcome it recorded. A caller sees it by repeating the command with its caller key.
    [Theory]
    [InlineData("read", OutpostOutcomeKindEnum.Failed)]
    [InlineData("write", OutpostOutcomeKindEnum.Unknown)]
    public async Task The_outcome_of_the_agent_replaces_the_outcome_that_the_server_set_after_the_run_limit(
        string mode,
        OutpostOutcomeKindEnum setByServer)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"calls_run_limit_replaced_{mode}",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        DateTimeOffset start = server.Clock.GetUtcNow();
        (Task<OutpostOutcome> call, OutpostCommandMessage command) = await HandOutAsync(
            server,
            agent,
            mode,
            "key-17",
            cancellationToken);
        await server.WaitForTimerAsync(start + server.Options.PollTimeout, cancellationToken);
        server.Clock.Advance(Due(server, start, mode) - start);
        Assert.Equal(setByServer, (await call).Kind);

        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("""{"rows":3}"""), cancellationToken);
        OutpostOutcome repeat = await TestCalls.CallAsync(server.Server, mode, "key-17", cancellationToken);

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, repeat.Kind);
        Assert.Equal(command.Id, repeat.CommandId);
        Assert.Equal("""{"rows":3}""", repeat.Result!.Value.GetRawText());
    }

    // The answer that handed the Write out was lost. The agent comes back after the run limit, far past the take
    // deadline plus the skew by its own clock, and its poll does not list the command: the command never reached it,
    // so it never ran. Expired frees the caller key, and a repeat creates a new command, which that poll takes.
    [Fact]
    public async Task A_poll_that_proves_a_write_never_reached_its_agent_turns_the_Unknown_that_the_server_set_into_Expired()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "calls_run_limit_never_reached",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        DateTimeOffset start = server.Clock.GetUtcNow();
        (Task<OutpostOutcome> write, OutpostCommandMessage command) = await HandOutAsync(
            server,
            agent,
            "write",
            "invoice-17",
            cancellationToken);
        await server.WaitForTimerAsync(start + server.Options.PollTimeout, cancellationToken);
        server.Clock.Advance(Due(server, start, "write") - start);
        OutpostOutcome unknown = await write;
        Assert.Equal(OutpostOutcomeKindEnum.Unknown, unknown.Kind);
        Assert.True(server.Clock.GetUtcNow() > command.Deadline + command.Skew);

        (Task<OutpostOutcome> repeat, OutpostCommandMessage next) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [Bases()]),
            () => TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);
        CommandRow? row = await server.Schema.FindCommandAsync(command.Id, cancellationToken);

        Assert.Equal(OutpostOutcomeKindEnum.Expired, row?.OutcomeKind);
        Assert.Null(row?.OutcomeCode);
        Assert.Null(row?.Outcome);
        Assert.NotEqual(command.Id, next.Id);

        await agent.ConfirmOutcomeAsync(next.Id, AgentOutcomes.Succeeded("""{"rows":3}"""), cancellationToken);
        OutpostOutcome outcome = await repeat;

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(next.Id, outcome.CommandId);
    }

    // Both modes with one free worker each, a run limit of 5 minutes for Read and 7 for Write.
    private static OutpostPollBase Bases()
    {
        return StoreCommands.Serving(TestCalls.BaseKey, 1, 1);
    }

    private static Task<(Task<OutpostOutcome> Call, OutpostCommandMessage Command)> HandOutAsync(
        LoopbackServer server,
        TestAgent agent,
        string mode,
        string callerKey,
        CancellationToken cancellationToken)
    {
        return server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [Bases()]),
            () => TestCalls.CallAsync(server.Server, mode, callerKey, cancellationToken),
            cancellationToken);
    }

    // The poll that handed the command out was recorded at the start, with the option's poll timeout as its hold.
    private static DateTimeOffset Due(LoopbackServer server, DateTimeOffset start, string mode)
    {
        TimeSpan runLimit = mode == "write" ? StoreCommands.WriteRunLimit : StoreCommands.ReadRunLimit;

        return start + server.Options.PollTimeout + server.Options.LeaseGrace + runLimit;
    }
}
