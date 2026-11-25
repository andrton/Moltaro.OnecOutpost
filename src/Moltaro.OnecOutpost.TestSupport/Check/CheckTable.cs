using System.Text.RegularExpressions;
using Xunit;

namespace Moltaro.OnecOutpost.TestSupport.Check;

// The table that the console's check writes, read back from its output for the console's tests
// and the lab's: the columns Check, Result and Reason, each row one line, its Check cell naming the line by its kind, as
// CheckLineEnum names it, and its subject, such as a base and an account, its Result passed or failed. A table of the
// console is drawn with borders, whose columns a | or a │ separates; a reason of several lines continues on the next
// lines of the table, with the other cells empty. Lines that are not rows of the table, such as the line of its end,
// are passed over.
internal static class CheckTable
{
    private static readonly char[] Separators = ['|', '│', '┃', '║'];

    // The rows of the table in their order; none when the output holds no table.
    public static IReadOnlyList<CheckRow> Rows(string output)
    {
        List<CheckRow> rows = [];

        foreach (string line in output.Split('\n'))
        {
            if (Cells(line) is not [string check, string result, string reason])
            {
                continue;
            }

            if (result is "passed" or "failed")
            {
                rows.Add(new CheckRow(check, result == "passed", reason));
            }
            else if (check.Length == 0 && result.Length == 0 && reason.Length > 0 && rows.Count > 0)
            {
                // A further line of the reason of the row above.
                rows[^1] = rows[^1] with { Reason = rows[^1].Reason + "\n" + reason };
            }
        }

        return rows;
    }

    // The one row of the kind whose Check cell names each part of the subject, such as the base and the account.
    public static CheckRow Row(IReadOnlyList<CheckRow> rows, string kind, params string[] subject)
    {
        List<CheckRow> found = [.. rows.Where(row => Names(row, kind, subject))];

        Assert.True(
            found.Count == 1,
            $"The table has {found.Count} rows of {kind} {string.Join(" ", subject)}, not one:\n{string.Join("\n", rows)}");

        return found[0];
    }

    // The rows of the kind, in their order.
    public static IReadOnlyList<CheckRow> RowsOf(IReadOnlyList<CheckRow> rows, string kind)
    {
        return [.. rows.Where(row => Names(row, kind, []))];
    }

    // Whether the Check cell of the row names the kind, as a word, and each part of the subject.
    public static bool Names(CheckRow row, string kind, params string[] subject)
    {
        return Regex.IsMatch(row.Check, $@"(^|\W){Regex.Escape(kind)}(\W|$)")
            && subject.All(part => row.Check.Contains(part, StringComparison.Ordinal));
    }

    // The cells of a line of the table between its borders, trimmed; none for a line that is not one of its rows. The
    // table of the console's metadata is drawn the same way.
    public static string[] Cells(string line)
    {
        string trimmed = line.TrimEnd('\r').Trim();

        if (trimmed.Length < 2 || Array.IndexOf(Separators, trimmed[0]) < 0 || Array.IndexOf(Separators, trimmed[^1]) < 0)
        {
            return [];
        }

        return [.. trimmed[1..^1].Split(Separators).Select(cell => cell.Trim())];
    }
}
