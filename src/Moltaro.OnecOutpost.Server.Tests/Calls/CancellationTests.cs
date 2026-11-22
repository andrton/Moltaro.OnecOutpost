using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.Server.Tests.Loopback;
using Moltaro.OnecOutpost.Server.Tests.Migrations;
using Moltaro.OnecOutpost.Server.Tests.Store;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Calls;

// Concept rule 12 over loopback HTTP: the caller decides how long it waits. A cancelled call ends with
// OperationCanceledException and leaves its command running, and a repeat with its caller key returns the outcome. A
// token that is cancelled already when the call is made gives a cancelled task, and the call changes nothing
// (IOutpostServer).
[Trait("Requires", "PostgreSQL")]
public sealed class CancellationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_cancelled_call_leaves_its_command_running_and_a_repeat_with_its_caller_key_returns_the_outcome()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "calls_cancelled_wait",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        using CancellationTokenSource caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        (Task<OutpostOutcome> cancelled, OutpostCommandMessage command) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]),
            () => TestCalls.CallAsync(server.Server, "write", "invoice-17", caller.Token),
            cancellationToken);

        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        CommandRow? row = await server.Schema.FindCommandAsync(command.Id, cancellationToken);
        Assert.Equal(CommandPhaseEnum.HandedOut, row?.Phase);
        Assert.Null(row?.OutcomeKind);

        await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("""{"rows":3}"""), cancellationToken);
        OutpostOutcome repeat = await TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken);

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, repeat.Kind);
        Assert.Equal(command.Id, repeat.CommandId);
    }

    // The agent is connected and its poll is held, so a call that went on would create a command.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("read")]
    [InlineData("write")]
    public async Task A_call_whose_token_is_cancelled_already_gives_a_cancelled_task_and_creates_no_command(string mode)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"calls_cancelled_before_{mode}",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        OutpostPollRequest poll = agent.NextPoll(server.Options.PollTimeout, [ReadAndWrite(TestCalls.BaseKey)]);
        await server.HoldPollAsync(agent, poll, cancellationToken);
        SchemaSnapshot before = await server.Schema.TakeSnapshotAsync(cancellationToken);

        Task<OutpostOutcome> call = TestCalls.CallAsync(server.Server, mode, "key-17", ArgumentChecks.Cancelled);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.True(call.IsCanceled);
        Assert.Equal(before.Lines, (await server.Schema.TakeSnapshotAsync(cancellationToken)).Lines);
    }
}
