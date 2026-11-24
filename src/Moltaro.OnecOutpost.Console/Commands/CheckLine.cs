namespace Moltaro.OnecOutpost.Console.Commands;

// One line of check: its kind, what it judged, such as a base and an account, whether it passed, and its reason, a line
// that needs one that failed failing as "not checked: {subject} failed". The reason never holds a password or text that
// the console has not verified.
internal sealed record CheckLine(CheckLineEnum Kind, string Subject, bool Passed, string Reason);
