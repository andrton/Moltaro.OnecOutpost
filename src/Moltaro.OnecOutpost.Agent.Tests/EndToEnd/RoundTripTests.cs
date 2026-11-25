using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Server;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using Moltaro.OnecOutpost.TestSupport.RunningAgent;

namespace Moltaro.OnecOutpost.Agent.Tests.EndToEnd;

// The goal of the S6 brief, over loopback against the server of S5: a Read and a Write from ReadAsync and WriteAsync to
// the fake worker and back, through the test proxy that policy.json names. The agent pairs with a token of the node,
// polls with numbered polls, checks and records the command, runs it on the fake worker with the script, the
// parameters and the caller key of the call, and sends its outcome until the node confirms it; the call returns that
// outcome. Agent and node share the fake clock, which does not move: the call wakes the held poll.
[Trait("Requires", "PostgreSQL")]
public sealed class RoundTripTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Theory]
    [InlineData("read", null)]
    [InlineData("write", "invoice-17")]
    public async Task A_call_reaches_the_fake_worker_through_the_proxy_named_in_policy_json_and_returns_its_outcome(
        string mode,
        string? callerKey)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(postgres, $"agent_round_trip_{mode}", cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(server, proxy, cancellationToken);
        await using HostRun run = agent.Start(server.Clock);
        await server.WaitForTimerAsync(server.Clock.GetUtcNow() + server.Options.PollTimeout, cancellationToken);

        Task<OutpostOutcome> call = await server.StartCallAsync(
            agent.Id,
            () => TestCalls.CallAsync(server.Server, mode, callerKey, cancellationToken),
            cancellationToken);
        OutpostOutcome outcome = await call;

        Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind);
        Assert.Equal(TestCalls.Parameters, outcome.Result!.Value.GetRawText());
        WorkerCommandLine line = Assert.Single(agent.Workers.Process(TestCalls.BaseKey, TestCommands.ModeOf(mode)).Commands);
        Assert.Equal(TestCalls.Script, line.Script);
        Assert.Equal(TestCalls.Parameters, line.ParametersJson);
        Assert.Equal(callerKey, line.CallerKey);

        Guid id = outcome.CommandId!.Value;
        await agent.Data.WaitForAsync(Operations.Is($"confirmed {id}"));
        Operations.AssertInOrder(Operations.Since(agent.Data, 0), $"received {id}", $"ended {id}", $"confirmed {id}");
        Assert.NotEmpty(proxy.Connects);
        Assert.All(proxy.Connects, connect => Assert.Equal($"CONNECT {TestProxy.Host}:{server.Address.Port} HTTP/1.1", connect));
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }
}
