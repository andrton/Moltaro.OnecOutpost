using System.Text;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeWorkers;

namespace Moltaro.OnecOutpost.Agent.Tests.Workers;

// What turns the executor's object into the outcome sent (§8 of docs/protocol.md). The agent checks the object by §2
// and §8 for the mode of its command, then by what the executor may
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
    [Theory]
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

    // The executor returns the size it measured beside its object, and a size other than the length
    // of the bytes the agent received is an outcome it cannot accept, a byte more as a byte less.
    [Theory]
    [InlineData("read", 1, "failed")]
    [InlineData("write", -1, "unknown")]
    public async Task A_size_other_than_the_length_of_the_bytes_gives_result_type(string modeWord, long off, string kindWord)
    {
        WorkerRun run = await RunAsync(modeWord, FakeRun.GivesSizeOff(SucceededInZurich, off));

        Assert.Equal(TestOutcomes.Own(TestOutcomes.KindOf(kindWord), "result-type"), run.Outcome);
    }

    // §8: anything the agent cannot read by §2 and §8, or that the mode of the command does not allow, becomes
    // result-type.
    [Theory]
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
    // and, for a Write, unknown with transaction-ended. The agent also accepts the codes the COM worker writes
    // itself. An outcome that an agent may send but neither of them hands over becomes result-type.
    [Theory]
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

    // The pool forwards the COM worker's own outcomes byte for byte, as it does the executor's. It accepts Failed
    // base-unreachable in either mode, Failed connection-lost for a Read, and Unknown connection-lost and
    // Unknown result-type for a Write. After base-unreachable and connection-lost the worker has ended, so it is dropped
    // and its mode has one worker fewer; after result-type, the worker's answer to a text of the executor with a line
    // break, its session is whole and it stays. The bytes have white space that the agent's writer does not write, so
    // that they show they were forwarded.
    [Theory]
    [InlineData("read", "failed", "base-unreachable", true)]
    [InlineData("write", "failed", "base-unreachable", true)]
    [InlineData("read", "failed", "connection-lost", true)]
    [InlineData("write", "unknown", "connection-lost", true)]
    [InlineData("write", "unknown", "result-type", false)]
    public async Task An_outcome_that_the_COM_worker_writes_itself_is_sent_byte_for_byte_and_a_worker_that_ended_is_dropped(
        string modeWord,
        string kindWord,
        string code,
        bool dropped)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        OutpostModeEnum mode = TestCommands.ModeOf(modeWord);
        byte[] outcome = Encoding.UTF8.GetBytes(
            $$"""{ "kind": "{{kindWord}}", "code": "{{code}}", "result": null, "errorText": null, "messages": [], "messagesTrimmed": false }""");
        await using TestPool test = TestPool.Create();
        test.Launcher.Runs = _ => FakeRun.Gives(outcome);
        test.OpenAll();
        OutpostCommandMessage command = test.Command(mode);

        WorkerRun run = await test.Pool.RunAsync(command, cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken);
        test.Pool.Release(command.Id);

        Assert.Equal(outcome, run.Outcome);
        Assert.Equal(dropped ? 0 : 1, test.Pool.Free("accounting", mode));
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
