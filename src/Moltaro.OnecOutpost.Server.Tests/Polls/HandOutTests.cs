using System.Text.Json;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Tests.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;

namespace Moltaro.OnecOutpost.Server.Tests.Polls;

// The hand-out of commands in the answer to a poll over loopback HTTP (§6.2 and §10 of docs/protocol.md, concept rules
// 8, 10 and 11): a poll takes at most `free` commands of each mode, oldest first, and a command handed out again counts
// against `free`; the answer is filled up to its limit of 64 MiB and never beyond. The commands are created by calls
// while the agent's poll has no free worker, each once the one before it was created, so that their order is known;
// a later poll with free workers then answers at once with what it takes.
[Trait("Requires", "PostgreSQL")]
public sealed class HandOutTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    // The length of an entry of `commands` without the base64url of its bytes and of its signature:
    // {"bytes":"","signature":""}.
    private const int EntryEnvelope = 27;

    // A signature of 64 bytes in base64url without padding.
    private const int SignatureText = 86;

    // The default of the option, which every poll of these tests carries.
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(25);

    // The guard of the calls of the test of 64 MiB, in place of the deadline of TestCalls, which is a guard against a
    // hang and states nothing. Each of its calls lasts while three commands of 16 MiB are created, handed out in answers
    // of up to 64 MiB and confirmed: about 7 seconds alone, and over a minute on a loaded machine. A correct
    // implementation meets this with a wide margin on a slow runner, and a call that never returns still fails the test.
    private static readonly TimeSpan LargeCallDeadline = TimeSpan.FromMinutes(5);

    // The order in which the calls create their commands: Reads without a caller key and Writes with one, each told by
    // the number of its script.
    private static readonly (string Mode, string? CallerKey, int Number)[] Created =
    [
        ("read", null, 1),
        ("read", null, 2),
        ("read", null, 3),
        ("write", "invoice-4", 4),
        ("write", "invoice-5", 5),
    ];

    // Three Reads and two Writes, told apart by their scripts. The answer to the second poll is lost, so the agent
    // lists none of its commands, and the third poll takes the same ones again; the fourth lists them, and takes the
    // rest.
    [Fact]
    public async Task A_poll_takes_at_most_free_commands_per_mode_oldest_first_and_a_command_handed_out_again_counts_against_free()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(postgres, "polls_hand_out_free", cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        await server.HoldPollAsync(agent, agent.NextPoll(PollTimeout, [Serving(0, 0)]), cancellationToken);

        List<Task<OutpostOutcome>> calls = [];

        foreach ((string mode, string? callerKey, int number) in Created)
        {
            calls.Add(await server.StartCallAsync(
                agent.Id,
                () => TestCalls.CallAsync(server.Server, mode, callerKey, ScriptOf(number), "{}", cancellationToken),
                cancellationToken));
        }

        IReadOnlyList<OutpostCommandMessage> taken = await PollAsync(agent, Serving(2, 1), [], cancellationToken);
        IReadOnlyList<OutpostCommandMessage> again = await PollAsync(agent, Serving(2, 1), [], cancellationToken);
        IReadOnlyList<OutpostCommandMessage> rest = await PollAsync(
            agent,
            Serving(2, 1),
            [.. again.Select(command => command.Id)],
            cancellationToken);

        Assert.Equal<string>([ScriptOf(1), ScriptOf(2), ScriptOf(4)], taken.Select(command => command.Script));
        Assert.Equal(taken.Select(command => command.Id), again.Select(command => command.Id));
        Assert.Equal<string>([ScriptOf(3), ScriptOf(5)], rest.Select(command => command.Script));

        foreach (OutpostCommandMessage command in again.Concat(rest))
        {
            await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("null"), cancellationToken);
        }

        Assert.All(await Task.WhenAll(calls), outcome => Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind));
    }

    // Three Reads of nearly the largest command, 16 MiB: two of the largest, and a third whose size makes the answer
    // with all three exactly 64 MiB, or one byte more. At the limit the answer holds all three; one byte beyond, it
    // holds the two oldest, and the next poll, which lists them, takes the third. Its sizes are computed as §6.2 and §7
    // write them, so the test asserts first that the answer at the limit is exactly the limit.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task An_answer_is_filled_with_the_oldest_commands_up_to_its_limit_of_64_MiB_and_never_beyond(int beyond)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using LoopbackServer server = await LoopbackServer.StartAsync(
            postgres,
            $"polls_hand_out_limit_{beyond}",
            options => options.CommandSizeLimit = OutpostProtocol.MaxCommandSizeLimit,
            cancellationToken);
        using TestAgent agent = await server.PairAgentAsync(cancellationToken);
        await server.HoldPollAsync(agent, agent.NextPoll(PollTimeout, [Serving(0, null)]), cancellationToken);
        int[] scriptLengths = ScriptLengths(server, agent.Id, beyond);

        List<Task<OutpostOutcome>> calls = [];

        foreach (int length in scriptLengths)
        {
            OutpostReadRequest request = new() { BaseKey = TestCalls.BaseKey, Script = new('x', length), Parameters = [] };

            calls.Add(await server.StartCallAsync(
                agent.Id,
                () => server.Server.ReadAsync(request, cancellationToken).WaitAsync(LargeCallDeadline),
                cancellationToken));
        }

        OutpostPollRequest poll = agent.NextPoll(PollTimeout, [Serving(3, null)]);
        AgentAnswer answer = await agent.PollAsync(poll, cancellationToken);
        IReadOnlyList<OutpostCommandMessage> taken = agent.ReadCommands(agent.ReadPollAnswer(poll, answer));
        List<OutpostCommandMessage> handedOut = [.. taken];

        if (beyond == 0)
        {
            Assert.Equal(OutpostProtocol.PollAnswerLimit, answer.Body.Length);
            Assert.Equal(scriptLengths, taken.Select(command => command.Script.Length));
        }
        else
        {
            Assert.True(answer.Body.Length < OutpostProtocol.PollAnswerLimit);
            Assert.Equal(scriptLengths[..2], taken.Select(command => command.Script.Length));

            IReadOnlyList<OutpostCommandMessage> next = await PollAsync(
                agent,
                Serving(3, null),
                [.. taken.Select(command => command.Id)],
                cancellationToken);

            Assert.Equal(scriptLengths[2..], next.Select(command => command.Script.Length));
            handedOut.AddRange(next);
        }

        foreach (OutpostCommandMessage command in handedOut)
        {
            await agent.ConfirmOutcomeAsync(command.Id, AgentOutcomes.Succeeded("null"), cancellationToken);
        }

        Assert.All(await Task.WhenAll(calls), outcome => Assert.Equal(OutpostOutcomeKindEnum.Succeeded, outcome.Kind));
    }

    private static string ScriptOf(int number)
    {
        return $"Result = {number};";
    }

    private static OutpostPollBase Serving(int readFree, int? writeFree)
    {
        return StoreCommands.Serving(TestCalls.BaseKey, readFree, writeFree);
    }

    // Sends the agent's next poll with the base and the received commands given, and gives the commands of its answer,
    // which comes at once when the poll takes any.
    private static async Task<IReadOnlyList<OutpostCommandMessage>> PollAsync(
        TestAgent agent,
        OutpostPollBase serving,
        IReadOnlyList<Guid> received,
        CancellationToken cancellationToken)
    {
        OutpostPollRequest poll = agent.NextPoll(PollTimeout, [serving], received);
        AgentAnswer answer = await agent.PollAsync(poll, cancellationToken);

        return agent.ReadCommands(agent.ReadPollAnswer(poll, answer));
    }

    // The lengths of the scripts of the three Reads. The answer is the answer without commands, of the length that the
    // codec writes for the agent's state and the options, plus an entry per command and a comma between two. An entry
    // is the base64url of the bytes of its command and of its signature in EntryEnvelope. The bytes of a command are
    // its script and the rest, whose length is fixed for a Read of this agent in this base without parameters. The
    // second script is shortened until the third can make the answer the limit and also one byte more, since the
    // lengths of base64url skip every fourth number; for beyond, the third makes it one byte more.
    private static int[] ScriptLengths(LoopbackServer server, Guid agentId, int beyond)
    {
        OutpostServerOptions options = server.Options;
        DateTimeOffset time = server.Clock.GetUtcNow();
        OutpostPollAnswer empty = new(time, null, options.PollTimeout, options.LeaseGrace, [TestCalls.BaseKey], [], []);
        long room = OutpostProtocol.PollAnswerLimit - OutpostCodec.Write(empty).Length;

        using JsonDocument parameters = JsonDocument.Parse("{}");
        OutpostCommandMessage withOneCharacter = new(
            OutpostProtocol.Version,
            Guid.NewGuid(),
            agentId,
            TestCalls.BaseKey,
            OutpostModeEnum.Read,
            null,
            "x",
            parameters.RootElement,
            time,
            time + options.TakeTimeout,
            options.ClockSkew,
            options.OutcomeSizeLimit);
        long rest = OutpostCodec.Write(withOneCharacter).Length - 1;

        // The size of a command is its script and the 2 bytes of {} (§7).
        int largest = OutpostProtocol.MaxCommandSizeLimit - 2;
        int second = largest;
        long third;

        do
        {
            long left = room - Entry(rest + largest) - Entry(rest + second) - 2;
            third = left - EntryEnvelope - SignatureText;
            second--;
        }
        while (third % 4 is 0 or 1);

        second++;
        long thirdBytes = (3 * (third + beyond)) / 4;

        Assert.Equal(third + beyond, Base64Length(thirdBytes));
        Assert.InRange(thirdBytes - rest, 1, largest);

        return [largest, second, (int)(thirdBytes - rest)];
    }

    private static long Entry(long bytes)
    {
        return EntryEnvelope + Base64Length(bytes) + SignatureText;
    }

    // The length of base64url without padding (RFC 4648 §5).
    private static long Base64Length(long bytes)
    {
        return ((4 * bytes) + 2) / 3;
    }
}
