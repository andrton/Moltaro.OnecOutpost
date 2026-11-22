using System.Text.Json;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Tests;

// Commands that passed the checks of rule 1, for the agent of PairedFolder: a Read or a Write of the base accounting,
// a Write with the caller key invoice-42, with a take deadline 30 seconds after it was issued.
internal static class TestCommands
{
    public static OutpostCommandMessage Of(
        Guid id,
        OutpostModeEnum mode,
        DateTimeOffset issued,
        string script = "Result = 1;",
        string parameters = "{}")
    {
        return new OutpostCommandMessage(
            OutpostProtocol.Version,
            id,
            PairedFolder.Agent,
            "accounting",
            mode,
            mode == OutpostModeEnum.Write ? "invoice-42" : null,
            script,
            Json(parameters),
            issued,
            issued + TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            16 * 1024 * 1024);
    }

    // The mode of its word, read or write: a theory names it so, since the protocol's types are internal.
    public static OutpostModeEnum ModeOf(string word)
    {
        return word == "read" ? OutpostModeEnum.Read : OutpostModeEnum.Write;
    }

    public static JsonElement Json(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text);

        return document.RootElement.Clone();
    }
}
