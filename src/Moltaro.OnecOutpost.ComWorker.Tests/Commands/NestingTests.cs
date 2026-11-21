using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests.Commands;

// The worker reads a line to a depth of 72 levels, the protocol's limit of a message (§2 and §10 of docs/protocol.md),
// so that parameters of 64 levels, the most a command carries, pass. The command line itself is level 1, so its
// parameters of n levels make a line of n + 1.
[Trait("Requires", "Windows")]
public sealed class NestingTests
{
    [Theory]
    [InlineData(64)]
    [InlineData(71)]
    public void Parameters_that_keep_the_line_within_72_levels_reach_the_executor(int levels)
    {
        FakeSession session = new();

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command(parameters: LineText.Nested(levels)).ToLine());

        Assert.Equal(WorkerExitCodes.Ended, run.ExitCode);
        FakeRun executorRun = Assert.Single(Assert.Single(session.Executors).Runs);
        Assert.Equal(LineText.Nested(levels), executorRun.ParametersJson);
    }

    [Fact]
    public void A_command_line_deeper_than_72_levels_cannot_be_read_and_ends_the_worker_with_exit_code_1()
    {
        FakeSession session = new();

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(session),
            LineText.Connection().ToLine(),
            LineText.Command(parameters: LineText.Nested(72)).ToLine());

        Assert.Equal(WorkerExitCodes.BadInput, run.ExitCode);
        Assert.Equal("{\"ready\":true}\n", run.Output.Text);
        Assert.Equal(0, session.OpenAttempts);
    }
}
