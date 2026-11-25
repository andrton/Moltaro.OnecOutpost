using System.Text;
using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests.OwnOutcomes;

// The outcomes the worker writes itself, in the form of the executor's line: the executor that does not open, the
// call of Run that throws, and a text of Run with a line break,
// which would shift every later outcome. Where a Read and a Write differ, a Read fails and a Write is unknown.
[Trait("Requires", "Windows")]
public sealed class OwnOutcomeTests
{
    // The mode, the text of the fake executor, and the kind the worker reports for it.
    public static TheoryData<string, string, string> TextsWithALineBreak => new()
    {
        { "Read", ExecutorLine.Succeeded.Replace(",\"outcome\":", ",\n\"outcome\":"), "failed" },
        { "Write", ExecutorLine.Succeeded.Replace(",\"outcome\":", ",\n\"outcome\":"), "unknown" },
        { "Read", ExecutorLine.Succeeded.Replace(",\"outcome\":", ",\r\n\"outcome\":"), "failed" },
        { "Write", ExecutorLine.Succeeded.Replace(",\"outcome\":", ",\r\n\"outcome\":"), "unknown" },
        { "Read", ExecutorLine.Succeeded + "\n", "failed" },
        { "Write", ExecutorLine.Succeeded + "\n", "unknown" },
    };

    [Theory]
    [InlineData("Read")]
    [InlineData("Write")]
    public void An_executor_that_does_not_open_gives_failed_base_unreachable_and_exit_code_2(string mode)
    {
        FakeSession session = new() { OpenFailure = new InvalidOperationException("The fake executor does not open.") };

        WorkerRun run = WorkerRun.Of(
            Enum.Parse<WorkerModeEnum>(mode),
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.NoSession, run.ExitCode);
        Assert.Equal(2, run.Output.Lines.Count);
        Assert.Equal("{\"ready\":true}", Encoding.UTF8.GetString(run.Output.Lines[0]));
        OwnOutcomeAssert.Written("failed", "base-unreachable", run.Output.Lines[1]);
        Assert.Equal(1, session.OpenAttempts);
    }

    // The worker ends after its base-unreachable line, so its diagnostics come before it.
    [Fact]
    public void The_diagnostics_of_an_executor_that_does_not_open_come_before_its_base_unreachable_line()
    {
        FakeSession session = new() { OpenFailure = new InvalidOperationException("The fake executor does not open.") };

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Read,
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command().ToLine());

        Assert.Contains("The fake executor does not open.", run.Diagnostics);
        Assert.Equal(1, run.OutputLinesBeforeDiagnostics);
        OwnOutcomeAssert.Written("failed", "base-unreachable", run.Output.Lines[1]);
    }

    [Theory]
    [InlineData("Read", "failed")]
    [InlineData("Write", "unknown")]
    public void A_run_that_throws_gives_connection_lost_and_exit_code_3(string mode, string kind)
    {
        FakeSession session = new(() => throw new InvalidOperationException("The fake session ended."));

        WorkerRun run = WorkerRun.Of(
            Enum.Parse<WorkerModeEnum>(mode),
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.SessionLost, run.ExitCode);
        Assert.Equal(2, run.Output.Lines.Count);
        OwnOutcomeAssert.Written(kind, "connection-lost", run.Output.Lines[1]);
        Assert.Single(session.Executors);
    }

    [Fact]
    public void The_text_of_the_failure_of_a_run_that_throws_goes_to_the_diagnostics_only()
    {
        FakeSession session = new(() => throw new InvalidOperationException("The fake session ended."));

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command().ToLine());

        Assert.Contains("The fake session ended.", run.Diagnostics);
        Assert.DoesNotContain("The fake session ended.", run.Output.Text);
    }

    // The session is whole, so the worker goes on: the next command runs in a fresh executor of the same session.
    [Theory]
    [MemberData(nameof(TextsWithALineBreak))]
    public void A_text_of_run_with_a_line_break_gives_result_type_and_the_worker_goes_on(
        string mode,
        string text,
        string kind)
    {
        FakeSession session = new(() => text, () => ExecutorLine.Succeeded);

        WorkerRun run = WorkerRun.Of(
            Enum.Parse<WorkerModeEnum>(mode),
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.Ended, run.ExitCode);
        Assert.Equal(3, run.Output.Lines.Count);
        OwnOutcomeAssert.Written(kind, "result-type", run.Output.Lines[1]);
        Assert.Equal(ExecutorLine.Succeeded, Encoding.UTF8.GetString(run.Output.Lines[2]));
        Assert.Equal(2, session.Executors.Count);
    }
}
