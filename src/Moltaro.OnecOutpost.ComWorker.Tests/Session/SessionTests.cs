using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests.Session;

// The connection line opens the one session of the worker, which answers {"ready":true} once it is open, or
// {"ready":false} and ends with exit code 2 when it does not open.
[Trait("Requires", "Windows")]
public sealed class SessionTests
{
    [Theory]
    [InlineData("Read")]
    [InlineData("Write")]
    public void A_worker_whose_session_opens_writes_ready_true(string mode)
    {
        FakeConnector connector = new(new FakeSession());

        WorkerRun run = WorkerRun.Of(Enum.Parse<WorkerModeEnum>(mode), connector, LineText.Connection().ToLine());

        Assert.Equal(WorkerExitCodes.Ended, run.ExitCode);
        Assert.Equal("{\"ready\":true}\n", run.Output.Text);
    }

    [Fact]
    public void The_connector_gets_the_location_the_user_and_the_password_of_the_connection_line_with_their_escapes_undone()
    {
        const string Location = "File=\"C:\\Bases\\Склад\";";
        const string User = "Оператор \"склада\"";
        const string Password = "p\"a\\s/s\tж";
        FakeConnector connector = new(new FakeSession());
        LineText connection = LineText.Connection()
            .With("location", LineText.Quote(Location))
            .With("user", LineText.Quote(User))
            .With("password", LineText.Quote(Password));

        WorkerRun.Of(WorkerModeEnum.Write, connector, connection.ToLine());

        Assert.Equal((Location, User, Password), Assert.Single(connector.Connections));
    }

    [Theory]
    [InlineData("Read")]
    [InlineData("Write")]
    public void A_session_that_does_not_open_gives_ready_false_and_exit_code_2_and_runs_no_command(string mode)
    {
        FakeSession session = new();
        FakeConnector connector = new(session) { Failure = new InvalidOperationException("The fake refused the session.") };

        WorkerRun run = WorkerRun.Of(
            Enum.Parse<WorkerModeEnum>(mode),
            connector,
            LineText.Connection().ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.NoSession, run.ExitCode);
        Assert.Equal("{\"ready\":false}\n", run.Output.Text);
        Assert.Equal(0, session.OpenAttempts);
    }

    // The console's check reads the diagnostics of a worker whose open gave false,
    // so they are written before {"ready":false}.
    [Fact]
    public void The_diagnostics_of_a_session_that_does_not_open_come_before_ready_false()
    {
        FakeConnector connector = new(new FakeSession())
        {
            Failure = new InvalidOperationException("The fake refused the session."),
        };

        WorkerRun run = WorkerRun.Of(WorkerModeEnum.Read, connector, LineText.Connection().ToLine());

        Assert.Equal("{\"ready\":false}\n", run.Output.Text);
        Assert.Contains("The fake refused the session.", run.Diagnostics);
        Assert.Equal(0, run.OutputLinesBeforeDiagnostics);
    }

    [Fact]
    public void The_text_of_the_failure_of_a_session_that_does_not_open_goes_to_the_diagnostics()
    {
        FakeConnector connector = new(new FakeSession())
        {
            Failure = new InvalidOperationException("The fake refused the session."),
        };

        WorkerRun run = WorkerRun.Of(WorkerModeEnum.Read, connector, LineText.Connection().ToLine());

        Assert.Contains("The fake refused the session.", run.Diagnostics);
    }

    [Fact]
    public void One_session_serves_every_command_of_the_worker_and_is_disposed_once_when_the_input_ends()
    {
        FakeSession session = new();
        FakeConnector connector = new(session);

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            connector,
            LineText.Connection().ToLine(),
            LineText.Command().ToLine(),
            LineText.Command().ToLine());

        Assert.Equal(WorkerExitCodes.Ended, run.ExitCode);
        Assert.Single(connector.Connections);
        Assert.Equal(2, session.Executors.Count);
        Assert.Equal(1, session.DisposeCount);
    }
}
