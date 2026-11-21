using System.Text;
using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests.Input;

// The end of the input ends the worker with exit code 0, once the command it runs has its outcome written, and also
// before the first line. A last line without its LF is a line (the brief of S9, "The lines").
[Trait("Requires", "Windows")]
public sealed class EndOfInputTests
{
    [Theory]
    [InlineData("Read")]
    [InlineData("Write")]
    public void The_end_of_the_input_before_the_first_line_ends_the_worker_with_exit_code_0_and_writes_nothing(string mode)
    {
        FakeConnector connector = new(new FakeSession());

        WorkerRun run = WorkerRun.Of(Enum.Parse<WorkerModeEnum>(mode), connector);

        Assert.Equal(WorkerExitCodes.Ended, run.ExitCode);
        Assert.Empty(run.Output.Bytes);
        Assert.Empty(connector.Connections);
    }

    [Fact]
    public void The_end_of_the_input_after_the_connection_line_ends_the_worker_with_exit_code_0_after_ready()
    {
        FakeSession session = new();

        WorkerRun run = WorkerRun.Of(WorkerModeEnum.Write, new FakeConnector(session), LineText.Connection().ToLine());

        Assert.Equal(WorkerExitCodes.Ended, run.ExitCode);
        Assert.Equal("{\"ready\":true}\n", run.Output.Text);
        Assert.Equal(0, session.OpenAttempts);
    }

    // AgentInput gives the end of the input as soon as the worker asks for more after the command line.
    [Theory]
    [InlineData("Read")]
    [InlineData("Write")]
    public void The_end_of_the_input_right_after_a_command_ends_the_worker_with_exit_code_0_once_its_outcome_is_written(
        string mode)
    {
        FakeSession session = new();

        WorkerRun run = WorkerRun.Of(
            Enum.Parse<WorkerModeEnum>(mode),
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.Ended, run.ExitCode);
        Assert.Equal(Encoding.UTF8.GetBytes("{\"ready\":true}\n" + ExecutorLine.Succeeded + "\n"), run.Output.Bytes);
    }

    [Fact]
    public void A_connection_line_without_its_LF_at_the_end_of_the_input_is_a_line()
    {
        FakeConnector connector = new(new FakeSession());

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Read,
            connector,
            LineText.LastLine(LineText.Connection().ToString()));

        Assert.Equal(WorkerExitCodes.Ended, run.ExitCode);
        Assert.Equal("{\"ready\":true}\n", run.Output.Text);
        Assert.Single(connector.Connections);
    }

    [Fact]
    public void A_command_line_without_its_LF_at_the_end_of_the_input_is_a_line()
    {
        FakeSession session = new();

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.LastLine(LineText.Command().ToString()));

        Assert.Equal(WorkerExitCodes.Ended, run.ExitCode);
        Assert.Equal(Encoding.UTF8.GetBytes("{\"ready\":true}\n" + ExecutorLine.Succeeded + "\n"), run.Output.Bytes);
        Assert.Single(session.Executors);
    }
}
