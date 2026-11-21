using System.Text.Json;

namespace Moltaro.OnecOutpost.ComWorker;

// Reads one line of the worker's input with System.Text.Json: a JSON object in UTF-8 whose members are exactly those
// its kind carries, each once, every one of the type its kind gives it. A line that breaks this throws FormatException,
// whose reason names the rule and quotes nothing of the line, since the first line carries the password.
internal static class LineJson
{
    // The protocol's limit of a message (§2 and §10 of docs/protocol.md), so that parameters of 64 levels pass.
    private const int MaxDepth = 72;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = MaxDepth };

    // The owner disposes of the document; the values that the readers below return do not depend on it.
    public static JsonDocument Parse(byte[] line)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(line, Options);
        }
        catch (JsonException failure)
        {
            throw new FormatException(
                $"The line is not JSON in UTF-8, or it nests deeper than {MaxDepth} levels (byte {failure.BytePositionInLine} of the line).");
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();

            throw new FormatException("The line is not a JSON object.");
        }

        return document;
    }

    // The values of the members of the object, in the order of the names: each name present once, and no other.
    public static JsonElement[] Members(JsonElement line, params string[] names)
    {
        JsonElement[] values = new JsonElement[names.Length];
        bool[] present = new bool[names.Length];

        foreach (JsonProperty member in line.EnumerateObject())
        {
            int index = Array.IndexOf(names, NameOf(member));

            if (index < 0)
            {
                throw new FormatException("The line has a member that it does not carry.");
            }

            if (present[index])
            {
                throw new FormatException($"The line has the member {names[index]} twice.");
            }

            present[index] = true;
            values[index] = member.Value;
        }

        int missing = Array.IndexOf(present, false);

        if (missing >= 0)
        {
            throw new FormatException($"The line has no member {names[missing]}.");
        }

        return values;
    }

    public static string ReadString(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"The member {name} is not a string.");
        }

        return Text(value, name);
    }

    public static string? ReadNullableString(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"The member {name} is neither a string nor null.");
        }

        return Text(value, name);
    }

    public static bool ReadBoolean(JsonElement value, string name)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException($"The member {name} is neither true nor false."),
        };
    }

    // An integer has no fraction and no exponent (§2 of docs/protocol.md).
    public static long ReadInteger(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long integer))
        {
            throw new FormatException($"The member {name} is not an integer.");
        }

        return integer;
    }

    // The text of an object member exactly as the line had it: its white space, escapes and numbers unchanged.
    public static string ReadObjectText(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"The member {name} is not an object.");
        }

        return Text(value, name);
    }

    // System.Text.Json refuses to turn bytes that are not UTF-8, or an escape of a lone surrogate, into a string.
    private static string Text(JsonElement value, string name)
    {
        try
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        }
        catch (InvalidOperationException)
        {
            throw new FormatException($"The member {name} holds text that is not valid UTF-8 or Unicode.");
        }
    }

    private static string NameOf(JsonProperty member)
    {
        try
        {
            return member.Name;
        }
        catch (InvalidOperationException)
        {
            throw new FormatException("The line has a member name that is not valid UTF-8 or Unicode.");
        }
    }
}
