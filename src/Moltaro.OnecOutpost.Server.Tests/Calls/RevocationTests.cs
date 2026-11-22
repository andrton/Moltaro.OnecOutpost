using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Tests.Loopback;
using Moltaro.OnecOutpost.Server.Tests.Store;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Server.Tests.Calls;

// Concept rule 13 over loopback HTTP: revoking an agent ends its commands, and each call that waits for one of them
// returns at once, with the codes of OutpostCodes. A command that no poll took is Refused with `revoked`; a taken one
// ends as rule 3 says: a Write as Unknown with `revoked`, a Read as Failed with `lost`. The clock does not move, so the
// calls return because the revocation woke them.
[Trait("Requires", "PostgreSQL")]
public sealed class RevocationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // A Read and a Write are taken by two polls, and a third poll, with no free worker, leaves a second Write waiting.
    // Each poll after the first has a shorter hold than the waits of the calls before it, so that the timer of its hold
    // tells that it is recorded, and lists the commands the agent received.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Revoking_an_agent_ends_its_commands_and_every_waiting_call_returns_the_code_of_its_command()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            "calls_revocation_ends_commands",
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);

        (Task<OutpostOutcome> read, OutpostCommandMessage readCommand) = await server.HandOutAsync(
            agent,
            agent.NextPoll(server.Options.PollTimeout, [StoreCommands.Serving(TestCalls.BaseKey, 1, 0)]),
            () => TestCalls.CallAsync(server.Server, "read", null, cancellationToken),
            cancellationToken);

        (Task<OutpostOutcome> write, OutpostCommandMessage writeCommand) = await server.HandOutAsync(
            agent,
            agent.NextPoll(
                TimeSpan.FromSeconds(20),
                [StoreCommands.Serving(TestCalls.BaseKey, 0, 1)],
                [readCommand.Id]),
            () => TestCalls.CallAsync(server.Server, "write", "invoice-17", cancellationToken),
            cancellationToken);

        OutpostPollRequest busy = agent.NextPoll(
            TimeSpan.FromSeconds(15),
            [StoreCommands.Serving(TestCalls.BaseKey, 0, 0)],
            [readCommand.Id, writeCommand.Id]);
        await server.HoldPollAsync(agent, busy, cancellationToken);
        Task<OutpostOutcome> waiting = await server.StartCallAsync(
            agent.Id,
            () => TestCalls.CallAsync(server.Server, "write", "invoice-18", cancellationToken),
            cancellationToken);

        Assert.True(await server.Server.RevokeAgentAsync(agent.Id, cancellationToken));

        OutpostOutcome readEnded = await read;
        OutpostOutcome writeEnded = await write;
        OutpostOutcome waitingEnded = await waiting;

        Assert.Equal(OutpostOutcomeKindEnum.Failed, readEnded.Kind);
        Assert.Equal(OutpostCodes.Lost, readEnded.Code);
        Assert.Equal(readCommand.Id, readEnded.CommandId);
        Assert.Equal(OutpostOutcomeKindEnum.Unknown, writeEnded.Kind);
        Assert.Equal(OutpostCodes.Revoked, writeEnded.Code);
        Assert.Equal(writeCommand.Id, writeEnded.CommandId);
        Assert.Equal(OutpostOutcomeKindEnum.Refused, waitingEnded.Kind);
        Assert.Equal(OutpostCodes.Revoked, waitingEnded.Code);
        Assert.NotNull(waitingEnded.CommandId);
    }
}
