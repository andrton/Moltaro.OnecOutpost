using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.OneC.Tests.Agent;

// Concept rule 3 and §5 on the lab (the S9b brief, "The lab tests"): the agent ends its COM workers with itself, and a
// Write that was running then is Unknown. Once a poll shows the looping Write as received and not yet confirmed, and its
// run began, the test stops the agent, which exits with Stopped; a second run of the agent on the data folder finds the
// Write received without an outcome and sends Unknown agent-stopped.
[SupportedOSPlatform("windows")]
public sealed class AgentStopTests
{
    [Fact]
    public async Task Stopping_the_agent_during_a_write_gives_unknown_agent_stopped_which_the_next_run_sends()
    {
        using LabAgent agent = LabAgent.Create();
        Guid write;

        await using (LabRun first = agent.Start())
        {
            Assert.True(await first.OpenedAsync(OutpostModeEnum.Write), first.WhyNotOpened(OutpostModeEnum.Write));
            write = first.Send(OutpostModeEnum.Write, TestBaseScripts.LoopForTenMinutes);
            await first.ReceivedAsync(write);
            await first.RanAsync(OutpostModeEnum.Write);

            Assert.Equal(AgentExitCodes.Stopped, await first.StopAsync());
        }

        await using LabRun second = agent.Start();
        OutpostOutcomeMessage outcome = await second.OutcomeAsync(write);

        Assert.Equal((OutpostAgentOutcomeKindEnum.Unknown, OutpostOutcomeCodes.AgentStopped), (outcome.Kind, outcome.Code));
        Assert.Equal(AgentExitCodes.Stopped, await second.StopAsync());
    }
}
