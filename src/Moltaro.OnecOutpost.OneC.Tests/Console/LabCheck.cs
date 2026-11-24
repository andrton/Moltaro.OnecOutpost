using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.TestSupport.Check;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.OneC.Tests.Console;

// One run of the console's check on the lab, as its output shows it: its exit code and the rows of its table, by kind
// and subject (CheckTable). The rows of a copy of a typical base name its 1C account and objects of its configuration,
// which no message of a test names: its assertions say which line, by its kind and a word the test gives, and count
// the lines by kind and result, never quoting a row.
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

    // The one row of the kind whose Check cell holds the subject, such as "typical read {account}", passed; a message
    // names only the line as the test calls it and the counts of the table.
    public void AssertPassedQuietly(string about, string line, CheckLineEnum kind, string subject)
    {
        string label = $"{kind} {subject}";
        List<CheckRow> found = [.. Rows.Where(row => row.Check == label)];

        Assert.True(found.Count == 1, $"{about}: the table has {found.Count} rows of {line}, not one; {Summary}");
        Assert.True(found[0].Passed, $"{about}: {line} did not pass; {Summary}");
    }

    // The lines of the table counted by kind and result, such as "Connection 2 passed, 0 failed".
    public string Summary => string.Join(
        "; ",
        Rows.GroupBy(KindOf).Select(kind => $"{kind.Key} {kind.Count(row => row.Passed)} passed, {kind.Count(row => !row.Passed)} failed"));

    // The kind of the row, the first word of its Check cell.
    public static string KindOf(CheckRow row)
    {
        return row.Check.Split(' ', 2)[0];
    }
}
