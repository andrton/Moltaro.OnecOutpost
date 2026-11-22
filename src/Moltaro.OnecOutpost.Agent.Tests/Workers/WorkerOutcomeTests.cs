using Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Workers;

// What turns the executor's object into the outcome sent (§8 of docs/protocol.md, the S6b brief, and S9's requirement
// on the size). The agent checks the object by §2 and §8 for the mode of its command, then by what the executor may
// hand it, and changes nothing: an object it cannot accept, or a size other than the length of its bytes, becomes
// Failed result-type for a Read and Unknown result-type for a Write, written as the agent writes its own outcomes; any
// other goes byte for byte, in the executor's own form.
public sealed class WorkerOutcomeTests
{
    // An object of the executor in its own form, with white space that the writer of §2 does not write, and a text
    // that is longer in UTF-8 bytes than in characters.
    private static readonly byte[] SucceededInZurich =
        """{ "kind": "succeeded", "code": null, "result": { "city": "Zürich" }, "errorText": null, "messages": [ "Done" ], "messagesTrimmed": false }"""u8
            .ToArray();

    // §8: the agent forwards the bytes of an object that the executor may hand it as they came, in either mode. The
    // run took no time on the clock.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("read", "succeeded")]
    [InlineData("read", "script-error")]
    [InlineData("write", "succeeded")]
    [InlineData("write", "transaction-ended")]
    public async Task An_object_the_executor_may_hand_over_is_sent_byte_for_byte(string modeWord, string name)
    {
        byte[] outcome = name switch
        {
            "succeeded" => SucceededInZurich,
            "script-error" => TestOutcomes.ScriptError(),
            _ => TestOutcomes.TransactionEnded(),
        };

        WorkerRun run = await RunAsync(modeWord, FakeRun.Gives(outcome));

        Assert.Equal(outcome, run.Outcome);
        Assert.Equal(0, run.DurationMs);
    }

    // S9's requirement: the executor returns the size it measured beside its object, and a size other than the length
    // of the bytes the agent received is an outcome it cannot accept, a byte more as a byte less.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("read", 1, "failed")]
    [InlineData("write", -1, "unknown")]
    public async Task A_size_other_than_the_length_of_the_bytes_gives_result_type(string modeWord, long off, string kindWord)
    {
        WorkerRun run = await RunAsync(modeWord, FakeRun.GivesSizeOff(SucceededInZurich, off));

        Assert.Equal(TestOutcomes.Own(TestOutcomes.KindOf(kindWord), "result-type"), run.Outcome);
    }

    // §8: anything the agent cannot read by §2 and §8, or that the mode of the command does not allow, becomes
    // result-type.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("read", "an Unknown, which a Read never is", "failed")]
    [InlineData("write", "bytes that are not JSON", "unknown")]
    public async Task An_object_that_section_8_refuses_for_the_mode_gives_result_type(
        string modeWord,
        string form,
        string kindWord)
    {
        byte[] outcome = form.StartsWith("an Unknown", StringComparison.Ordinal)
            ? TestOutcomes.TransactionEnded()
            : """{"kind":"succeeded","code":null,"result":"""u8.ToArray();

        WorkerRun run = await RunAsync(modeWord, FakeRun.Gives(outcome));

        Assert.Equal(TestOutcomes.Own(TestOutcomes.KindOf(kindWord), "result-type"), run.Outcome);
    }

    // §8, "The executor": it may hand over only succeeded, failed with script-error, result-type or result-too-large,
    // and, for a Write, unknown with transaction-ended. An outcome that an agent may send but the executor may not hand
    // over becomes result-type, until S9b admits the codes that S9's worker writes itself.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("read", "failed", "run-limit", "failed")]
    [InlineData("read", "refused", "not-served", "failed")]
    [InlineData("write", "unknown", "agent-stopped", "unknown")]
    public async Task An_object_the_executor_may_not_hand_over_gives_result_type(
        string modeWord,
        string givenKind,
        string givenCode,
        string kindWord)
    {
        WorkerRun run = await RunAsync(modeWord, FakeRun.Gives(TestOutcomes.Own(TestOutcomes.KindOf(givenKind), givenCode)));

        Assert.Equal(TestOutcomes.Own(TestOutcomes.KindOf(kindWord), "result-type"), run.Outcome);
    }

    // A run of a command of accounting in a mode, on a pool whose workers opened, whose run gives this.
    private static async Task<WorkerRun> RunAsync(string modeWord, FakeRun given)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using TestPool test = TestPool.Create();
        test.Launcher.Runs = _ => given;
        test.OpenAll();

        return await test.Pool
            .RunAsync(test.Command(TestCommands.ModeOf(modeWord)), cancellationToken)
            .WaitAsync(TestTimes.Deadline, cancellationToken);
    }
}
