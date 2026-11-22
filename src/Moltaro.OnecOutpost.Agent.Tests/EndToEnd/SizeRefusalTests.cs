using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Server;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;

namespace Moltaro.OnecOutpost.Agent.Tests.EndToEnd;

// An outcome refused for its size, over loopback against the server of S5 (concept rule 11, §6.3 and §9 of
// docs/protocol.md): by the server's code, for an outcome beyond the outcome limit of its command, 64 KiB here, and by
// a bare HTTP 413 with an HTML body on the path, from a hop whose body limit is 1 KiB. The outbox file and a second
// ended hold Unknown result-too-large for a Write and Failed result-too-large for a Read, and that is what is sent: the
// call returns it.
[Trait("Requires", "PostgreSQL")]
public sealed class SizeRefusalTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Theory]
    [InlineData("read", null, "server", "failed")]
    [InlineData("write", "invoice-17", "server", "unknown")]
    [InlineData("read", null, "path", "failed")]
    [InlineData("write", "invoice-17", "path", "unknown")]
    public async Task An_outcome_refused_for_its_size_is_replaced_recorded_and_sent_and_the_call_returns_the_replacement(
        string mode,
        string? callerKey,
        string refusedBy,
        string kindWord)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"agent_size_{mode}_{refusedBy}",
            options => options.OutcomeSizeLimit = OutpostProtocol.MinOutcomeSizeLimit,
            cancellationToken);
        await using TestProxy proxy = TestProxy.Start();
        LoopbackAgent agent = await LoopbackAgent.PairAsync(server, proxy, cancellationToken);

        // Beyond the limit of the command, and within the body limit of the endpoint, its limit plus 4 KiB; or beyond
        // the body limit of the hop.
        int length = refusedBy == "server" ? OutpostProtocol.MinOutcomeSizeLimit + 1024 : 2048;
        agent.Workers.Runs = _ => FakeRun.Gives(LoopbackAgent.Large(length));

        if (refusedBy == "path")
        {
            server.OutcomeLimitOnThePath = 1024;
        }

        await using HostRun run = agent.Start(server.Clock);
        await server.WaitForTimerAsync(server.Clock.GetUtcNow() + server.Options.PollTimeout, cancellationToken);

        Task<OutpostOutcome> call = await server.StartCallAsync(
            agent.Id,
            () => TestCalls.CallAsync(server.Server, mode, callerKey, cancellationToken),
            cancellationToken);
        OutpostOutcome outcome = await call;

        Assert.Equal(Enum.Parse<OutpostOutcomeKindEnum>(kindWord, ignoreCase: true), outcome.Kind);
        Assert.Equal(OutpostCodes.ResultTooLarge, outcome.Code);

        Guid id = outcome.CommandId!.Value;
        await agent.Data.WaitForAsync(Operations.Is($"confirmed {id}"));
        List<JsonElement> ended = [.. AuditEntries.AppendedSince(agent.Data, 0).Where(entry => AuditEntries.Kind(entry) == "ended")];
        Assert.Equal(2, ended.Count);
        Assert.Equal("succeeded", ended[0].GetProperty("outcome").GetString());
        Assert.Equal(kindWord, ended[1].GetProperty("outcome").GetString());
        Assert.Equal(OutpostCodes.ResultTooLarge, ended[1].GetProperty("code").GetString());
        byte[] replaced = agent.Data.Changes.Last(change => change.Line == $"replace {Operations.OutboxFile(id)}").Bytes;
        Assert.Equal(TestOutcomes.Own(TestOutcomes.KindOf(kindWord), OutpostCodes.ResultTooLarge), replaced);
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }
}
