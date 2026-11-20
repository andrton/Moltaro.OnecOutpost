using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Moltaro.OnecOutpost.Server;

// The rule of the parameters of a command and the rule of its size, which the checks of ReadAsync and WriteAsync run
// after the rules of the other names.
//
// The size is that of the script in UTF-8, plus that of the parameters written as compact JSON in UTF-8. Characters
// outside ASCII count as they are, never as escapes; only what JSON requires is escaped: '"' and '\' as two
// characters, U+0008, U+0009, U+000A, U+000C and U+000D as two, and the other characters below U+0020 as six. The
// built-in encoders of System.Text.Json escape characters outside the Basic Multilingual Plane even when relaxed, so
// the count walks the parameters instead of measuring a serialized text.
internal static class CommandChecks
{
    internal static void Check(string script, JsonObject? parameters, int commandSizeLimit)
    {
        long size = Encoding.UTF8.GetByteCount(script);

        if (parameters is not null)
        {
            // Written as JSON when the call is made, as the contract says. Parsing the text again gives every string
            // unescaped, whatever the writer escaped.
            using JsonDocument written = JsonDocument.Parse(parameters.ToJsonString());

            size += CountValue(written.RootElement);
        }

        if (size > commandSizeLimit)
        {
            throw new ArgumentException(
                $"The command is {size} bytes, more than the limit of {commandSizeLimit} bytes: its script in UTF-8 and its parameters written as JSON.",
                "request");
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

        // The quotes and the characters in UTF-8, then what the escapes add.
        long size = 2 + Encoding.UTF8.GetByteCount(text);

        foreach (char character in text)
        {
            if (character is '"' or '\\' or '\b' or '\t' or '\n' or '\f' or '\r')
            {
                size += 1;
            }
            else if (character < ' ')
            {
                size += 5;
            }
        }

        return size;
    }
}
