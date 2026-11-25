using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests.Output;

// The standard output carries only the worker's JSON replies, one UTF-8 object per line, with LF and without a BOM.
// Whatever the worker has to say besides goes to its diagnostics.
[Trait("Requires", "Windows")]
public sealed class StandardOutputTests
{
    // Every kind of line the worker writes: ready, an outcome of the executor, an outcome of its own after which it goes
    // on, and one after which it ends.
    [Fact]
    public void The_standard_output_carries_nothing_but_the_lines()
    {
        FakeSession session = new(
            () => ExecutorLine.Succeeded,
            () => ExecutorLine.Succeeded + "\n",
            () => throw new InvalidOperationException("The fake session ended."));

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.SessionLost, run.ExitCode);

        byte[] output = run.Output.Bytes;
        Assert.Equal((byte)'{', output[0]);
        Assert.Equal((byte)'\n', output[^1]);

        IReadOnlyList<byte[]> lines = run.Output.Lines;
        Assert.Equal(4, lines.Count);
        Assert.Equal("{\"ready\":true}", Encoding.UTF8.GetString(lines[0]));
        Assert.Equal(ExecutorLine.Succeeded, Encoding.UTF8.GetString(lines[1]));
        OwnOutcomeAssert.Written("unknown", "result-type", lines[2]);
        OwnOutcomeAssert.Written("unknown", "connection-lost", lines[3]);

        foreach (byte[] line in lines)
        {
            using JsonDocument document = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }

        Assert.NotEmpty(run.Diagnostics);
    }
}
