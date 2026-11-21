using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests.SafeMode;

// A Read always runs in safe mode, and a Write runs in safe mode unless the agent says otherwise for the base (D12): the
// worker opens each executor with the safe mode of its connection line, and a Read worker refuses to run without it.
[Trait("Requires", "Windows")]
public sealed class SafeModeTests
{
    [Fact]
    public void A_read_worker_told_to_run_without_safe_mode_ends_with_exit_code_1_and_writes_nothing()
    {
        FakeSession session = new();
        FakeConnector connector = new(session);

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Read,
            connector,
            LineText.Connection(safeMode: false).ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.BadInput, run.ExitCode);
        Assert.Empty(run.Output.Bytes);
        Assert.Equal(0, session.OpenAttempts);
        Assert.False(string.IsNullOrWhiteSpace(run.Diagnostics));
    }

    [Fact]
    public void A_read_worker_opens_each_executor_from_the_executor_of_its_connection_line_in_safe_mode()
    {
        FakeSession session = new();

        WorkerRun.Of(
            WorkerModeEnum.Read,
            new FakeConnector(session),
            LineText.Connection(safeMode: true).ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(2, session.Executors.Count);
        Assert.All(session.Executors, executor => Assert.Equal(LineText.ExecutorPath, executor.Path));
        Assert.All(session.Executors, executor => Assert.True(executor.SafeMode));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_write_worker_opens_each_executor_with_the_safe_mode_of_its_connection_line(bool safeMode)
    {
        FakeSession session = new();

        WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(session),
            LineText.Connection(safeMode).ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(2, session.Executors.Count);
        Assert.All(session.Executors, executor => Assert.Equal(LineText.ExecutorPath, executor.Path));
        Assert.All(session.Executors, executor => Assert.Equal(safeMode, executor.SafeMode));
    }
}
