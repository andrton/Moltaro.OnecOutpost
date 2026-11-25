using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;
using Moltaro.OnecOutpost.TestSupport.FakeWorkers;

namespace Moltaro.OnecOutpost.Agent.Tests.Loop;

// Outcomes are sent until they are confirmed (concept rules 3 and 11, §6.3 and §9 of docs/protocol.md): one at a
// time, the files of the outbox in turn, the start's first. A lost answer sends the
// same bytes again later: after it the next file waits a growing delay of its own. After a restart, the outcomes that
// the start recorded are sent: Unknown agent-stopped for a Write and Failed lost for a Read that were received and never
// ended, and a file without its ended as it is.
public sealed class OutcomeTests
{
    // The polls go on meanwhile; the first stays open, as a poll does while the server holds it.
    [Fact]
    public async Task Outcomes_are_sent_one_at_a_time_the_files_in_turn_until_each_is_confirmed()
    {
        PairedFolder folder = PairedFolder.Create();
        Guid succeeded = EarlierRun.Ended(folder, OutpostModeEnum.Read, TestOutcomes.Succeeded());
        Guid failed = EarlierRun.Ended(folder, OutpostModeEnum.Read, TestOutcomes.ScriptError());
        Dictionary<Guid, byte[]> files = new() { [succeeded] = TestOutcomes.Succeeded(), [failed] = TestOutcomes.ScriptError() };
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        await host.NextPollAsync();

        ServerExchange first = await host.NextOutcomeAsync();
        Guid lost = first.ReadOutcome().Command;
        Assert.Equal(files[lost], first.OutcomeBytes());
        first.AnswerBare(503);
        await host.PassAsync(TimeSpan.FromSeconds(3));

        ServerExchange second = await host.NextOutcomeAsync();
        Guid other = second.ReadOutcome().Command;
        Assert.NotEqual(lost, other);
        Assert.Equal(files[other], second.OutcomeBytes());
        second.Confirm();

        ServerExchange third = await host.NextOutcomeAsync();
        Assert.Equal(lost, third.ReadOutcome().Command);
        Assert.Equal(files[lost], third.OutcomeBytes());
        third.Confirm();
        await host.Data.WaitForAsync(change => change.Line == $"delete {Operations.OutboxFile(lost)}");

        Assert.Equal(1, host.Server.MostOutcomesAtOnce);
        Operations.AssertInOrder(Operations.Since(host.Data, 0), $"confirmed {other}", $"delete {Operations.OutboxFile(other)}");
        Operations.AssertInOrder(Operations.Since(host.Data, 0), $"confirmed {lost}", $"delete {Operations.OutboxFile(lost)}");
    }

    // The earlier run received a Write and a Read and stopped before they ended, and wrote the outcome file of another
    // Read but not its ended. Every one of them is listed as received and sent.
    [Fact]
    public async Task After_a_restart_the_outcomes_that_the_start_recorded_are_sent()
    {
        PairedFolder folder = PairedFolder.Create();
        Guid write = EarlierRun.ReceivedOnly(folder, OutpostModeEnum.Write);
        Guid read = EarlierRun.ReceivedOnly(folder, OutpostModeEnum.Read);
        Guid withoutEnded = EarlierRun.ReceivedOnly(folder, OutpostModeEnum.Read);
        folder.Data.Outcomes.Seed($"{withoutEnded}.json", TestOutcomes.Succeeded());
        Dictionary<Guid, byte[]> expected = new()
        {
            [write] = TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Unknown, OutpostOutcomeCodes.AgentStopped),
            [read] = TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Failed, OutpostOutcomeCodes.Lost),
            [withoutEnded] = TestOutcomes.Succeeded(),
        };
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        Assert.Equal(expected.Keys.Order(), (await host.NextPollAsync()).ReadPoll().Received.Order());

        Dictionary<Guid, byte[]> sent = [];

        for (int index = 0; index < expected.Count; index++)
        {
            ServerExchange outcome = await host.NextOutcomeAsync();
            sent.Add(outcome.ReadOutcome().Command, outcome.OutcomeBytes());
            outcome.Confirm();
        }

        Assert.Equal(expected.Keys.Order(), sent.Keys.Order());
        Assert.All(expected, file => Assert.Equal(file.Value, sent[file.Key]));
    }
}
