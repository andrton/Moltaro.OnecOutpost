using System.Text;
using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests.Input;

// A line the worker cannot read — not JSON, a member missing, unknown or of a wrong type — ends it with exit code 1 and
// a reason on the standard error. It is not skipped, and the worker writes nothing to the standard output for it (the
// brief of S9, "The lines").
[Trait("Requires", "Windows")]
public sealed class UnreadableLineTests
{
    public static TheoryData<string> ConnectionLinesThatCannotBeRead =>
    [
        "",
        "not JSON",
        "{",
        "[]",
        "null",
        LineText.Connection().Without("location").ToString(),
        LineText.Connection().Without("user").ToString(),
        LineText.Connection().Without("password").ToString(),
        LineText.Connection().Without("safeMode").ToString(),
        LineText.Connection().Without("executor").ToString(),
        LineText.Connection().With("database", "\"outpost_test\"").ToString(),
        LineText.Connection().With("location", "1").ToString(),
        LineText.Connection().With("user", "null").ToString(),
        LineText.Connection().With("password", "[]").ToString(),
        LineText.Connection().With("safeMode", "\"true\"").ToString(),
        LineText.Connection().With("safeMode", "null").ToString(),
        LineText.Connection().With("executor", "{}").ToString(),
    ];

    public static TheoryData<string> CommandLinesThatCannotBeRead =>
    [
        "",
        "not JSON",
        "{\"script\":",
        "[]",
        LineText.Command().Without("script").ToString(),
        LineText.Command().Without("parameters").ToString(),
        LineText.Command().Without("callerKey").ToString(),
        LineText.Command().Without("outcomeLimit").ToString(),
        LineText.Command().With("mode", "\"read\"").ToString(),
        LineText.Command().With("script", "1").ToString(),
        LineText.Command().With("script", "null").ToString(),
        LineText.Command().With("parameters", "[]").ToString(),
        LineText.Command().With("parameters", "\"{}\"").ToString(),
        LineText.Command().With("parameters", "null").ToString(),
        LineText.Command().With("callerKey", "7").ToString(),
        LineText.Command().With("callerKey", "true").ToString(),
        LineText.Command().With("outcomeLimit", "\"16777216\"").ToString(),
        LineText.Command().With("outcomeLimit", "1.5").ToString(),
        LineText.Command().With("outcomeLimit", "null").ToString(),
    ];

    [Theory]
    [MemberData(nameof(ConnectionLinesThatCannotBeRead))]
    public void A_connection_line_that_cannot_be_read_ends_the_worker_with_exit_code_1_and_a_reason_and_writes_nothing(
        string line)
    {
        FakeConnector connector = new(new FakeSession());

        WorkerRun run = WorkerRun.Of(WorkerModeEnum.Write, connector, LineText.Line(line), LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.BadInput, run.ExitCode);
        Assert.Empty(run.Output.Bytes);
        Assert.Empty(connector.Connections);
        Assert.False(string.IsNullOrWhiteSpace(run.Diagnostics));
    }

    // The command before the line keeps its outcome; the line and the command after it run nothing.
    [Theory]
    [MemberData(nameof(CommandLinesThatCannotBeRead))]
    public void A_command_line_that_cannot_be_read_ends_the_worker_with_exit_code_1_and_a_reason_and_is_not_skipped(
        string line)
    {
        FakeSession session = new();

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command().ToLine(),
            LineText.Line(line),
            LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.BadInput, run.ExitCode);
        Assert.Equal(Encoding.UTF8.GetBytes("{\"ready\":true}\n" + ExecutorLine.Succeeded + "\n"), run.Output.Bytes);
        Assert.Equal(1, session.OpenAttempts);
        Assert.False(string.IsNullOrWhiteSpace(run.Diagnostics));
    }
}
