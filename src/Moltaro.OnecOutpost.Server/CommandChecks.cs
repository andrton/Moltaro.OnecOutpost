using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Moltaro.OnecOutpost.Server;

// The rule of the parameters of a command and the rule of its size, which the checks of ReadAsync and WriteAsync run
// after the rules of the other names.
//
// The size is that of the script plus that of the parameters written as compact JSON, in UTF-8 with characters outside
// ASCII as they are, not escaped. One rule for both, with no escape counted anywhere: the script is its UTF-8 bytes;
// in the parameters every string, names included, is its UTF-8 bytes plus its two quotes, and structure and numbers
// count as written. Whether the escapes of JSON count is fixed by docs/protocol.md in S2, which fixes the exact count.
// The built-in encoders of System.Text.Json escape characters outside the Basic Multilingual Plane even when relaxed,
// so the count walks the parameters instead of measuring a serialized text.
internal static class CommandChecks
{
    // The levels a JSON message may nest, the outer object being level 1. The protocol fixes it for every message, so
    // it is not an option (Q5).
    private const int MaxNesting = 64;

    internal static void Check(string script, JsonObject? parameters, int commandSizeLimit)
    {
        long size = Encoding.UTF8.GetByteCount(script);

        if (parameters is not null)
        {
            using JsonDocument written = Write(parameters);

            size += CountValue(written.RootElement);
        }

        if (size > commandSizeLimit)
        {
            throw new ArgumentException(
                $"The command is {size} bytes, more than the limit of {commandSizeLimit} bytes: its script in UTF-8 and its parameters written as JSON.",
                "request");
        }
    }

    // Written as JSON when the call is made, as the contract says. Parsing the text again gives every string unescaped,
    // whatever the writer escaped. Parameters that cannot be written as JSON are the caller's mistake: the writer
    // refuses a number that is not finite with ArgumentException and a nesting deeper than its own limit of 1000
    // levels with InvalidOperationException, and the reader a nesting deeper than MaxNesting with JsonException.
    private static JsonDocument Write(JsonObject parameters)
    {
        try
        {
            return JsonDocument.Parse(parameters.ToJsonString(), new JsonDocumentOptions { MaxDepth = MaxNesting });
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            throw new ArgumentException(
                $"The parameters cannot be written as JSON: {exception.Message}",
                nameof(OutpostReadRequest.Parameters),
                exception);
        }
    }

    private static long CountValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    // The braces, and a comma between two members.
                    long size = 2;
                    int count = 0;

                    foreach (JsonProperty member in value.EnumerateObject())
                    {
                        // The name, its colon and the value.
                        size += CountString(member.Name) + 1 + CountValue(member.Value);
                        count++;
                    }

                    return size + Math.Max(count - 1, 0);
                }

            case JsonValueKind.Array:
                {
                    // The brackets, and a comma between two items.
                    long size = 2;
                    int count = 0;

                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        size += CountValue(item);
                        count++;
                    }

                    return size + Math.Max(count - 1, 0);
                }

            case JsonValueKind.String:
                return CountString(value.GetString()!);

            default:
                // A number, true, false or null: ASCII, counted as written.
                return value.GetRawText().Length;
        }
    }

    // Enforces the rule of the parameters on every string of them, names of members included, since it reads each one.
    private static long CountString(string text)
    {
        if (text.Contains('\0'))
        {
            throw new ArgumentException(
                "No string in the parameters may hold U+0000, which PostgreSQL cannot store.",
                nameof(OutpostReadRequest.Parameters));
        }

        // The two quotes and the characters in UTF-8.
        return 2 + Encoding.UTF8.GetByteCount(text);
    }
}
