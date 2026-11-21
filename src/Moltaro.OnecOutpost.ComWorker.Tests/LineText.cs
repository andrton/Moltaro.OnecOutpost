using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Moltaro.OnecOutpost.ComWorker.Tests;

// The text of one line of the worker's input, a JSON object whose members are written in order, each with its value as
// raw JSON. The tests of the lines that cannot be read start from a line that keeps every rule and change one member of
// it, since a correct writer can never produce what the worker must refuse. Nothing here uses the code of the worker.
internal sealed class LineText
{
    public const string Location = "Srvr=\"srv-1c:1541\";Ref=\"outpost_test\";";

    public const string User = "outpost-write";

    // A password that no other text of a test holds, and that JSON writes as it is, so that a search of the bytes of an
    // output finds it in any form the worker could write it.
    public const string Password = "Pw-4f9Kq2Lx7Zt";

    public const string ExecutorPath = @"C:\Outpost\OutpostExecutor.epf";

    public const string Script = "Result = 1;";

    public const long OutcomeLimit = 16777216;

    private readonly List<(string Name, string Value)> _members;

    public LineText(params (string Name, string Value)[] members)
    {
        _members = [.. members];
    }

    // The connection line of the internal contract: the base, the account of the mode, the safe mode and the executor.
    public static LineText Connection(bool safeMode = true)
    {
        return new LineText(
            ("location", Quote(Location)),
            ("user", Quote(User)),
            ("password", Quote(Password)),
            ("safeMode", safeMode ? "true" : "false"),
            ("executor", Quote(ExecutorPath)));
    }

    // A command line of the internal contract, with no caller key unless one is given.
    public static LineText Command(
        string script = Script,
        string parameters = "{}",
        string callerKey = "null",
        long outcomeLimit = OutcomeLimit)
    {
        return new LineText(
            ("script", Quote(script)),
            ("parameters", parameters),
            ("callerKey", callerKey),
            ("outcomeLimit", outcomeLimit.ToString(CultureInfo.InvariantCulture)));
    }

    // The same line with the member set to the value: in its place when the line has it, at the end otherwise.
    public LineText With(string name, string value)
    {
        LineText changed = new([.. _members]);
        int index = changed._members.FindIndex(member => member.Name == name);

        if (index < 0)
        {
            changed._members.Add((name, value));
        }
        else
        {
            changed._members[index] = (name, value);
        }

        return changed;
    }

    public LineText Without(string name)
    {
        LineText changed = new([.. _members]);
        changed._members.RemoveAll(member => member.Name == name);

        return changed;
    }

    public override string ToString()
    {
        return "{" + string.Join(",", _members.Select(member => Quote(member.Name) + ":" + member.Value)) + "}";
    }

    // The line as the agent writes it: UTF-8 without a BOM, ended with LF.
    public byte[] ToLine()
    {
        return Line(ToString());
    }

    public static byte[] Line(string text)
    {
        return Encoding.UTF8.GetBytes(text + "\n");
    }

    // A last line without its LF.
    public static byte[] LastLine(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    // A JSON string, escaped as System.Text.Json escapes it: quotes, backslashes, control characters and every character
    // outside ASCII as escapes, so that the worker has to undo them.
    public static string Quote(string text)
    {
        return JsonSerializer.Serialize(text);
    }

    // An object nested the given number of levels, the outer object being level 1: each object but the innermost, which
    // is empty, holds the next under the name "level".
    public static string Nested(int levels)
    {
        StringBuilder text = new();

        for (int level = 1; level < levels; level++)
        {
            text.Append("{\"level\":");
        }

        text.Append("{}");
        text.Append('}', levels - 1);

        return text.ToString();
    }
}
