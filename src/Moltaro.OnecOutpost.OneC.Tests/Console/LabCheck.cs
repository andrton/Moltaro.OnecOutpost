using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.TestSupport.Check;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.OneC.Tests.Console;

// One run of the console's check on the lab, as its output shows it: its exit code and the rows of its table, by kind
// and subject (CheckTable).
internal sealed class LabCheck
{
    public LabCheck(CommandAppResult result)
    {
        Result = result;
        Rows = CheckTable.Rows(result.Output);
    }

    public CommandAppResult Result { get; }

    public IReadOnlyList<CheckRow> Rows { get; }

    public int ExitCode => Result.ExitCode;

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

    // The row passed; otherwise the assertion says why, with the whole table.
    public void AssertPassed(string about, CheckLineEnum kind, params string[] subject)
    {
        CheckRow row = Row(kind, subject);

        Assert.True(row.Passed, $"{about}: the line {row} did not pass; the table:\n{string.Join("\n", Rows)}");
    }
}
