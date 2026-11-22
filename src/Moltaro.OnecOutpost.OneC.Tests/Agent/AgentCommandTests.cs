using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.OneC.Tests.Agent;

// The acceptance of S9b on the lab: a Read and a Write travel through the real agent and its COM workers to the test
// base and back, with the server of the test. The Write creates a product with a name the test generated, and the Read
// counts the products of that name.
[SupportedOSPlatform("windows")]
public sealed class AgentCommandTests
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_write_and_a_read_through_the_agent_reach_the_test_base_and_return_their_outcomes()
    {
        string parameters = new JsonObject { ["description"] = TestBaseScripts.NewDescription() }.ToJsonString();
        using LabAgent agent = LabAgent.Create();
        await using LabRun run = agent.Start();
        Assert.True(await run.OpenedAsync(OutpostModeEnum.Read));
        Assert.True(await run.OpenedAsync(OutpostModeEnum.Write));

        OutpostOutcomeMessage written = await run.OutcomeAsync(run.Send(OutpostModeEnum.Write, TestBaseScripts.CreateProduct, parameters));
        OutpostOutcomeMessage counted = await run.OutcomeAsync(run.Send(OutpostModeEnum.Read, TestBaseScripts.CountProducts, parameters));

        Assert.Equal(OutpostAgentOutcomeKindEnum.Succeeded, written.Kind);
        Assert.Equal(OutpostAgentOutcomeKindEnum.Succeeded, counted.Kind);
        Assert.Equal(1, counted.Result!.Value.GetInt32());
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }
}
