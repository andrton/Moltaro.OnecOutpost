using System.Text.Json;

namespace Moltaro.OnecOutpost.ComWorker;

// One command line of the worker's input, as IOneCExecutor.Run takes it: the script, the text of the parameters as the
// line had it, the caller key or null, and the outcome limit in bytes.
internal sealed record WorkerCommand(string Script, string ParametersJson, string? CallerKey, long OutcomeLimit)
{
    // Throws FormatException, with a reason, when the line cannot be read.
    public static WorkerCommand Read(byte[] line)
    {
        using JsonDocument document = LineJson.Parse(line);
        JsonElement[] members = LineJson.Members(
            document.RootElement,
            "script",
            "parameters",
            "callerKey",
            "outcomeLimit");

        return new WorkerCommand(
            LineJson.ReadString(members[0], "script"),
            LineJson.ReadObjectText(members[1], "parameters"),
            LineJson.ReadNullableString(members[2], "callerKey"),
            LineJson.ReadInteger(members[3], "outcomeLimit"));
    }
}
