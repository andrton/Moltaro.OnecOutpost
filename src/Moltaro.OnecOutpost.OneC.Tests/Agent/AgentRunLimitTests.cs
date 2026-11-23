using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.OneC.Tests.Agent;

// Concept rule 9 and §5 on the lab, through the real agent and its COM workers (the S9b brief, "The lab tests"): a
// script that exceeds the run limit of its mode ends with its worker, a Read Failed run-limit and a Write Unknown
// run-limit. Each slot then starts its worker again after a tenth of retryDelayMax, and 1C gives the new worker a
// session, in which a command of the same mode succeeds. The scripts loop by the clock of 1C, and the test moves the
// agent's fake clock past the run limit only once it observed both runs begin and their limits armed.
[SupportedOSPlatform("windows")]
public sealed class AgentRunLimitTests
{
    [Fact]
    public async Task A_script_beyond_its_run_limit_fails_a_read_leaves_a_write_unknown_and_the_worker_started_again_runs_the_next_command()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string parameters = new JsonObject { ["description"] = TestBaseScripts.NewDescription() }.ToJsonString();
        using LabAgent agent = LabAgent.Create();
        await using LabRun run = agent.Start();
        Assert.True(await run.OpenedAsync(OutpostModeEnum.Read), run.WhyNotOpened(OutpostModeEnum.Read));
        Assert.True(await run.OpenedAsync(OutpostModeEnum.Write), run.WhyNotOpened(OutpostModeEnum.Write));

        Guid read = run.Send(OutpostModeEnum.Read, TestBaseScripts.LoopForTenMinutes);
        Guid write = run.Send(OutpostModeEnum.Write, TestBaseScripts.LoopForTenMinutes);
        await run.RanAsync(OutpostModeEnum.Read);
        await run.RanAsync(OutpostModeEnum.Write);
        DateTimeOffset now = agent.Clock.GetUtcNow();
        await agent.Clock.WaitForTimersAsync(now + LabAgent.RunLimit, 2, cancellationToken).WaitAsync(LabAgent.Deadline, cancellationToken);
        agent.Clock.Advance(LabAgent.RunLimit);
        OutpostOutcomeMessage readEnded = await run.OutcomeAsync(read);
        OutpostOutcomeMessage writeEnded = await run.OutcomeAsync(write);

        Assert.Equal((OutpostAgentOutcomeKindEnum.Failed, OutpostOutcomeCodes.RunLimit), (readEnded.Kind, readEnded.Code));
        Assert.Equal((OutpostAgentOutcomeKindEnum.Unknown, OutpostOutcomeCodes.RunLimit), (writeEnded.Kind, writeEnded.Code));

        TimeSpan delay = LabAgent.RetryDelayMax / 10;
        now = agent.Clock.GetUtcNow();
        await agent.Clock.WaitForTimersAsync(now + delay, 2, cancellationToken).WaitAsync(LabAgent.Deadline, cancellationToken);
        agent.Clock.Advance(delay);
        Assert.True(await run.OpenedAsync(OutpostModeEnum.Read, 2));
        Assert.True(await run.OpenedAsync(OutpostModeEnum.Write, 2));

        OutpostOutcomeMessage written = await run.OutcomeAsync(run.Send(OutpostModeEnum.Write, TestBaseScripts.CreateProduct, parameters));
        OutpostOutcomeMessage counted = await run.OutcomeAsync(run.Send(OutpostModeEnum.Read, TestBaseScripts.CountProducts, parameters));

        Assert.Equal(OutpostAgentOutcomeKindEnum.Succeeded, written.Kind);
        Assert.Equal(OutpostAgentOutcomeKindEnum.Succeeded, counted.Kind);
        Assert.Equal(1, counted.Result!.Value.GetInt32());
        await run.RanAsync(OutpostModeEnum.Write, 2);
        await run.RanAsync(OutpostModeEnum.Read, 2);
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }
}
