using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Moltaro.OnecOutpost.OneC.Tests.Workers;

// One outcome line of the worker as the agent receives it: {"size":<number>,"outcome":<the outcome object of §8 of
// docs/protocol.md>}. Size is the length the executor measured; OutcomeBytes are the bytes of the outcome object as
// they arrived, which the agent forwards and the server measures.
internal sealed class OutcomeLine
{
    public required long Size { get; init; }

    public required byte[] OutcomeBytes { get; init; }

    public required string Kind { get; init; }

    public required string? Code { get; init; }

    public required JsonElement Result { get; init; }

    public required string? ErrorText { get; init; }

    public required IReadOnlyList<string> Messages { get; init; }

    public required bool MessagesTrimmed { get; init; }

    public static OutcomeLine Read(byte[] line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        JsonElement outcome = root.GetProperty("outcome");

        return new OutcomeLine
        {
            Size = root.GetProperty("size").GetInt64(),
            OutcomeBytes = JsonMarshal.GetRawUtf8Value(outcome).ToArray(),
            Kind = outcome.GetProperty("kind").GetString()!,
            Code = outcome.GetProperty("code").GetString(),
            Result = outcome.GetProperty("result").Clone(),
            ErrorText = outcome.GetProperty("errorText").GetString(),
            Messages = [.. outcome.GetProperty("messages").EnumerateArray().Select(message => message.GetString()!)],
            MessagesTrimmed = outcome.GetProperty("messagesTrimmed").GetBoolean(),
        };
    }

    // Asserts the kind and the code of the outcome, and shows the whole outcome when either differs.
    public void AssertIs(string kind, string? code)
    {
        Assert.True(Kind == kind && Code == code, $"Expected {kind} {code ?? "without a code"}, received {this}");
    }

    public override string ToString()
    {
        return Encoding.UTF8.GetString(OutcomeBytes);
    }
}
