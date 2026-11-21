using System.Runtime.InteropServices;
using System.Text.Json;

namespace Moltaro.OnecOutpost.ComWorker.Tests;

// The outcomes the worker writes itself are in the form of the executor's line, {"size":<number>,"outcome":<object>},
// with the six members of the outcome object of §8 of docs/protocol.md: result null, errorText null, messages empty and
// messagesTrimmed false. The size is the length in bytes of the outcome object as it was written.
internal static class OwnOutcomeAssert
{
    public static void Written(string kind, string code, byte[] line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;

        Assert.Equal(["outcome", "size"], root.EnumerateObject().Select(member => member.Name).Order());

        JsonElement outcome = root.GetProperty("outcome");
        Assert.Equal(JsonMarshal.GetRawUtf8Value(outcome).Length, root.GetProperty("size").GetInt64());

        Assert.Equal(
            ["code", "errorText", "kind", "messages", "messagesTrimmed", "result"],
            outcome.EnumerateObject().Select(member => member.Name).Order());

        Assert.Equal(kind, outcome.GetProperty("kind").GetString());
        Assert.Equal(code, outcome.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("result").ValueKind);
        Assert.Equal(JsonValueKind.Null, outcome.GetProperty("errorText").ValueKind);
        Assert.Equal(JsonValueKind.Array, outcome.GetProperty("messages").ValueKind);
        Assert.Equal(0, outcome.GetProperty("messages").GetArrayLength());
        Assert.Equal(JsonValueKind.False, outcome.GetProperty("messagesTrimmed").ValueKind);
    }
}
