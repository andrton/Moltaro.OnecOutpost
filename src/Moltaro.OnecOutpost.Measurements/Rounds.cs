using System.Diagnostics;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server;
using Moltaro.OnecOutpost.TestSupport.Loopback;
using Xunit;

namespace Moltaro.OnecOutpost.Measurements;

// The rounds of one case. In each, a Read goes from a call on one node to a poll held on another, or on the same, and
// its outcome goes back from the agent to the waiting caller. A round measures the two times of S7's brief:
//
// - to the poll: from the moment the application makes the call, with the poll already held, to the moment the poll
//   answer that carries the command has reached the agent, its body read and not yet verified;
// - to the caller: from the moment the agent sends the outcome, with the caller already waiting, to the moment the
//   call's task completes with it.
//
// The agent flushes nothing to disk, so neither time includes the flushes of the agent program, which come between the
// two. The clock of the nodes is the fake clock of the tests, which never moves here: a hop that waited on a timer of
// the options would never end, and the round would fail at LoopbackServer.Deadline instead of reporting a figure.
internal static class Rounds
{
    // A poll and a call are measured while they wait on their signal. The timer of each tells that it has begun to wait;
    // this pause lets it pass the Joined of its subscription first, as a poll or a call that waits for seconds has. It
    // lies outside both times.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(10);

    // What the agent returns for every Read: its result 1, as the script of TestCalls returns.
    private static readonly OutpostOutcomeMessage Succeeded = new(
        OutpostAgentOutcomeKindEnum.Succeeded,
        null,
        JsonDocument.Parse("1").RootElement.Clone(),
        null,
        [],
        false);

    // Runs the warm-up rounds, whose times are dropped, then the measured ones, with an agent paired on the node of the
    // poll.
    public static async Task<(List<TimeSpan> ToPoll, List<TimeSpan> ToCaller)> MeasureAsync(
        LoopbackServer callNode,
        LoopbackServer pollNode,
        int warmUp,
        int count,
        CancellationToken cancellationToken)
    {
        using TestAgent agent = await pollNode.PairAgentAsync(cancellationToken);

        List<TimeSpan> toPoll = [];
        List<TimeSpan> toCaller = [];

        for (int round = 0; round < warmUp + count; round++)
        {
            (TimeSpan ToPoll, TimeSpan ToCaller) times = await RoundAsync(callNode, pollNode, agent, cancellationToken);

            if (round >= warmUp)
            {
                toPoll.Add(times.ToPoll);
                toCaller.Add(times.ToCaller);
            }
        }

        return (toPoll, toCaller);
    }

    private static async Task<(TimeSpan ToPoll, TimeSpan ToCaller)> RoundAsync(
        LoopbackServer callNode,
        LoopbackServer pollNode,
        TestAgent agent,
        CancellationToken cancellationToken)
    {
        OutpostPollRequest poll = agent.NextPoll(
            pollNode.Options.PollTimeout,
            [PollRequests.ReadOnly(TestCalls.BaseKey)]);

        Task<AgentAnswer> held = await pollNode.HoldPollAsync(agent, poll, cancellationToken);
        await Task.Delay(Settle, cancellationToken);

        // The time to the poll begins as the application makes the call.
        long called = Stopwatch.GetTimestamp();
        Task<OutpostOutcome> call = TestCalls.CallAsync(callNode.Server, "read", null, cancellationToken);

        AgentAnswer answer = await held;

        // It ends as the agent has the poll answer.
        long reached = Stopwatch.GetTimestamp();

        OutpostCommandMessage command = Assert.Single(agent.ReadCommands(agent.ReadPollAnswer(poll, answer)));

        // Once the poll's request has ended on its node its hold timer is gone, so the next timer due within
        // PollTimeout is the call's wait for its outcome.
        await pollNode.WaitForEndOfAsync(answer, cancellationToken);
        await callNode.WaitForTimerAsync(callNode.Clock.GetUtcNow() + callNode.Options.PollTimeout, cancellationToken);
        await Task.Delay(Settle, cancellationToken);

        // The time to the caller ends as the call completes, whether before or after the agent reads the answer to its
        // outcome request.
        Task<long> answered = call.ContinueWith(
            _ => Stopwatch.GetTimestamp(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // It begins as the agent sends the outcome.
        long sent = Stopwatch.GetTimestamp();
        await agent.ConfirmOutcomeAsync(command.Id, Succeeded, cancellationToken);

        long ended = await answered;
        OutpostOutcome outcome = await call;

        if (outcome.Kind != OutpostOutcomeKindEnum.Succeeded || outcome.CommandId != command.Id)
        {
            throw new InvalidOperationException($"The call ended as {outcome.Kind} {outcome.Code}.");
        }

        return (Stopwatch.GetElapsedTime(called, reached), Stopwatch.GetElapsedTime(sent, ended));
    }
}
