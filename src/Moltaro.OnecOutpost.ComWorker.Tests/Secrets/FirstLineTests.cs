using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests.Secrets;

// The first input line carries the password, so the worker never writes it, also when it cannot read it and so cannot
// know which part of it is the password.
[Trait("Requires", "Windows")]
public sealed class FirstLineTests
{
    // Connection lines with the password that the worker cannot read: cut off after the password, so that it is no
    // JSON, or with a member of a wrong type, unknown or missing.
    public static TheoryData<string> ConnectionLinesThatCannotBeRead =>
    [
        CutAfterThePassword(LineText.Connection().ToString()),
        LineText.Connection().With("safeMode", "\"yes\"").ToString(),
        LineText.Connection().With("database", "\"outpost_test\"").ToString(),
        LineText.Connection().Without("executor").ToString(),
    ];

    // The executor fails its first Run, so that the worker has something to write to its diagnostics.
    [Fact]
    public void The_first_input_line_appears_in_no_output()
    {
        string connection = LineText.Connection().ToString();
        FakeSession session = new(() => throw new InvalidOperationException("The fake session ended."));

        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(session),
            LineText.Line(connection),
            LineText.Command().ToLine());

        Assert.NotEmpty(run.Diagnostics);
        Assert.DoesNotContain(connection, run.Diagnostics);
        Assert.DoesNotContain(connection, run.Output.Text);
    }

    [Theory]
    [MemberData(nameof(ConnectionLinesThatCannotBeRead))]
    public void A_first_line_that_cannot_be_read_appears_in_no_output_and_neither_does_its_password(string connection)
    {
        WorkerRun run = WorkerRun.Of(
            WorkerModeEnum.Write,
            new FakeConnector(new FakeSession()),
            LineText.Line(connection));

        Assert.Equal(WorkerExitCodes.BadInput, run.ExitCode);
        Assert.DoesNotContain(connection, run.Diagnostics);
        Assert.DoesNotContain(LineText.Password, run.Diagnostics);
        Assert.Empty(run.Output.Bytes);
    }

    private static string CutAfterThePassword(string line)
    {
        int end = line.IndexOf(LineText.Password, StringComparison.Ordinal) + LineText.Password.Length + 1;

        return line[..end];
    }
}
