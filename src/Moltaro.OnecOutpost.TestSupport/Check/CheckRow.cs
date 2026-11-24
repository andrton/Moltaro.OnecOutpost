namespace Moltaro.OnecOutpost.TestSupport.Check;

// One row of the table of the console's check, as its output shows it: the cell Check, which names the line by its kind
// and its subject, whether the line passed, and its reason, the lines of a reason that spans several joined by LF.
internal sealed record CheckRow(string Check, bool Passed, string Reason)
{
    // The reason of a line that was not checked, since a line it needs failed.
    public bool NotChecked => Reason.StartsWith("not checked: ", StringComparison.Ordinal);

    public override string ToString()
    {
        return $"{Check} | {(Passed ? "passed" : "failed")} | {Reason}";
    }
}
