using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport.Check;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// One run of check on a test's machine, as its output shows it: the rows of its table (CheckTable), by kind and
// subject, and how it ended: Every line passed. and Success when every line passed, else
// CheckFailed (19): {n} of {m} lines failed, a line that was not checked counting as failed.
internal sealed class CheckRun
{
    public CheckRun(CommandAppResult result)
    {
        Result = result;
        Rows = CheckTable.Rows(result.Output);
    }

    public CommandAppResult Result { get; }

    public IReadOnlyList<CheckRow> Rows { get; }

    public int ExitCode => Result.ExitCode;

    public string Output => Result.Output;

    // The one row of the kind whose Check cell names each part of the subject.
    public CheckRow Row(CheckLineEnum kind, params string[] subject)
    {
        return CheckTable.Row(Rows, kind.ToString(), subject);
    }

    // The rows of the kind, in their order.
    public IReadOnlyList<CheckRow> RowsOf(CheckLineEnum kind)
    {
        return CheckTable.RowsOf(Rows, kind.ToString());
    }

    // The reason of the row, which passed.
    public string Passed(CheckLineEnum kind, params string[] subject)
    {
        CheckRow row = Row(kind, subject);
        Assert.True(row.Passed, $"The line did not pass: {row}");

        return row.Reason;
    }

    // The reason of the row, which failed.
    public string Failed(CheckLineEnum kind, params string[] subject)
    {
        CheckRow row = Row(kind, subject);
        Assert.False(row.Passed, $"The line passed: {row}");

        return row.Reason;
    }

    // The row failed as not checked, since the line that its reason names, by a part of its subject, failed.
    public void AssertNotChecked(CheckLineEnum kind, string input, params string[] subject)
    {
        string reason = Failed(kind, subject);

        Assert.StartsWith("not checked: ", reason, StringComparison.Ordinal);
        Assert.EndsWith(" failed", reason, StringComparison.Ordinal);
        Assert.Contains(input, reason, StringComparison.Ordinal);
    }

    // The run ended with Success, every line passed.
    public void AssertSucceeded()
    {
        Assert.NotEmpty(Rows);
        Assert.All(Rows, row => Assert.True(row.Passed, $"The line did not pass: {row}"));
        Assert.Equal(ConsoleExitCodes.Success, ExitCode);
        Assert.Contains("Every line passed.", TestMachine.LinesOf(Result));
    }

    // The run ended with CheckFailed, counting the lines that failed among all of them.
    public void AssertCheckFailed()
    {
        int failed = Rows.Count(row => !row.Passed);

        Assert.True(failed > 0, "No line failed.");
        Assert.Equal(ConsoleExitCodes.CheckFailed, ExitCode);
        Assert.Contains(
            $"CheckFailed (19): {failed} of {Rows.Count} lines failed",
            TestMachine.LinesOf(Result).Select(line => line.TrimEnd()));
    }
}
