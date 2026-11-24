namespace Moltaro.OnecOutpost.Console.Commands;

// One line of check: its kind, what it judged, such as a base and an account, whether it passed, and its reason, a line
// that needs one that failed failing as "not checked: {subject} failed". The reason never holds a password or text that
// the console has not verified. check and the gate of its rule, which metadata runs too, make their lines here.
internal sealed record CheckLine(CheckLineEnum Kind, string Subject, bool Passed, string Reason)
{
    // The name of the line in the cell Check of the table: its kind and its subject.
    public string Name => Subject.Length == 0 ? Kind.ToString() : $"{Kind} {Subject}";

    public static CheckLine Pass(CheckLineEnum kind, string subject, string reason)
    {
        return new CheckLine(kind, subject, true, reason);
    }

    public static CheckLine Fail(CheckLineEnum kind, string subject, string reason)
    {
        return new CheckLine(kind, subject, false, reason);
    }

    // A line that needs the input, which failed.
    public static CheckLine NotChecked(CheckLineEnum kind, string subject, CheckLine input)
    {
        return Fail(kind, subject, $"not checked: {input.Name} failed");
    }

    // The first of the lines that failed, or null.
    public static CheckLine? FirstFailed(params CheckLine?[] inputs)
    {
        return inputs.FirstOrDefault(input => input is { Passed: false });
    }
}
