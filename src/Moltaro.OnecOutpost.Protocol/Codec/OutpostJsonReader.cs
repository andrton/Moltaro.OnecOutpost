using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Moltaro.OnecOutpost.Protocol.Codec;

// The reading of §2 of docs/protocol.md, which every reader of the codec uses: the JSON text, the rules of names and
// strings at every depth, the members of an object, and the forms of the values of §2. Each refuses what breaks §2 with
// OutpostProtocolException and `form`. The rules of one message are its reader's, in OutpostCodec.
//
// A message is parsed with ParseMessage. The two readers that look into a message before the rest of it is checked,
// the reader of a command for its `protocol` and `id` and the reader of the answer to `pair` for its server key, use
// ParseJson, TryGetMember and the Try readers, and CheckText afterwards.
internal static class OutpostJsonReader
{
    private static readonly JsonDocumentOptions MessageOptions = new() { MaxDepth = OutpostProtocol.MaxMessageDepth };

    public static OutpostProtocolException Form(string reason)
    {
        return new OutpostProtocolException(OutpostProtocolCodes.Form, reason);
    }

    // One JSON text of RFC 8259 in UTF-8, at most 72 levels deep, and nothing after it. The parser refuses a byte
    // order mark as the start of a value. JsonDocument reads the memory it is given in place, without a copy; the
    // caller disposes the document.
    public static JsonDocument ParseJson(ReadOnlyMemory<byte> body)
    {
        try
        {
            return JsonDocument.Parse(body, MessageOptions);
        }
        catch (JsonException)
        {
            throw Form("The body is not one JSON text of at most 72 levels in UTF-8.");
        }
    }

    // ParseJson, then CheckText over the whole message.
    public static JsonDocument ParseMessage(ReadOnlyMemory<byte> body)
    {
        JsonDocument document = ParseJson(body);

        try
        {
            CheckText(document.RootElement);
        }
        catch (OutpostProtocolException)
        {
            document.Dispose();
            throw;
        }

        return document;
    }

    // The rules of §2 for names and strings, at every depth, inside parameters and results too: no name twice in one
    // object, the names compared after unescaping, and no name or string with U+0000, a lone surrogate or bytes that
    // are not UTF-8.
    public static void CheckText(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                HashSet<string> names = new(StringComparer.Ordinal);

                foreach (JsonProperty member in value.EnumerateObject())
                {
                    string name = CheckedText(NameOf(member));

                    if (!names.Add(name))
                    {
                        throw Form("A name appears twice in one object.");
                    }

                    CheckText(member.Value);
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in value.EnumerateArray())
                {
                    CheckText(item);
                }

                break;

            case JsonValueKind.String:
                CheckedText(TextOf(value));
                break;
        }
    }

    // The object, when it has exactly these members. CheckText has refused a name that appears twice, so members of
    // these names only, as many as there are names, are each of them once.
    public static JsonElement Members(JsonElement value, params ReadOnlySpan<string> names)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Form($"An object with the members {string.Join(", ", names)} was expected.");
        }

        int count = 0;

        foreach (JsonProperty member in value.EnumerateObject())
        {
            if (!names.Contains(member.Name))
            {
                throw Form($"An object with the members {string.Join(", ", names)} has another member.");
            }

            count++;
        }

        if (count != names.Length)
        {
            throw Form($"An object with the members {string.Join(", ", names)} misses one of them.");
        }

        return value;
    }

    // The value of the one member of this name in an object, looked up before CheckText. False when the value is no
    // object or has the member not exactly once; a name that is no valid text is no name looked for.
    public static bool TryGetMember(JsonElement value, string name, out JsonElement member)
    {
        member = default;

        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        int count = 0;

        foreach (JsonProperty candidate in value.EnumerateObject())
        {
            if (NameOf(candidate) == name)
            {
                member = candidate.Value;
                count++;
            }
        }

        return count == 1;
    }

    // An integer of §2: a JSON number without a fraction and without an exponent, within a long.
    public static bool TryReadInteger(JsonElement value, out long integer)
    {
        integer = 0;

        if (value.ValueKind != JsonValueKind.Number || value.GetRawText().AsSpan().ContainsAny('.', 'e', 'E'))
        {
            return false;
        }

        return value.TryGetInt64(out integer);
    }

    // The text of a string. It may be called before CheckText: a string that is no valid text, holding bytes that are
    // not UTF-8 or a lone surrogate, is no text.
    public static bool TryReadText(JsonElement value, [NotNullWhen(true)] out string? text)
    {
        text = value.ValueKind == JsonValueKind.String ? TextOf(value) : null;

        return text is not null;
    }

    // An identity of §2. It may be called before CheckText: a string that is no valid text is no identity.
    public static bool TryReadId(JsonElement value, out Guid id)
    {
        id = Guid.Empty;

        return TryReadText(value, out string? text) && OutpostForms.TryReadId(text, out id);
    }

    // A binary value of §2. It may be called before CheckText: a string that is no valid text is no binary value.
    public static bool TryReadBytes(JsonElement value, [NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;

        return TryReadText(value, out string? text) && OutpostForms.TryReadBase64Url(text, out bytes);
    }

    // The readers of a member of an object whose members were checked: each refuses `null` and a value of the wrong
    // type or form, unless its name says that it reads a nullable value.
    public static string ReadString(JsonElement message, string name)
    {
        JsonElement value = message.GetProperty(name);

        if (value.ValueKind != JsonValueKind.String)
        {
            throw Form($"The member {name} is not a string.");
        }

        return value.GetString()!;
    }

    public static string? ReadNullableString(JsonElement message, string name)
    {
        return message.GetProperty(name).ValueKind == JsonValueKind.Null ? null : ReadString(message, name);
    }

    // Every range includes its ends.
    public static long ReadInteger(JsonElement message, string name, long min, long max)
    {
        if (!TryReadInteger(message.GetProperty(name), out long integer))
        {
            throw Form($"The member {name} is not an integer without a fraction and without an exponent.");
        }

        if (integer < min || integer > max)
        {
            throw Form($"The member {name} is outside its range, {min} to {max}.");
        }

        return integer;
    }

    public static long? ReadNullableInteger(JsonElement message, string name, long min, long max)
    {
        return message.GetProperty(name).ValueKind == JsonValueKind.Null ? null : ReadInteger(message, name, min, max);
    }

    // A duration of §2, whole milliseconds, within a range that includes its ends.
    public static TimeSpan ReadDuration(JsonElement message, string name, TimeSpan min, TimeSpan max)
    {
        long milliseconds = ReadInteger(
            message,
            name,
            min.Ticks / TimeSpan.TicksPerMillisecond,
            max.Ticks / TimeSpan.TicksPerMillisecond);

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    // A time of §2: UTC, exactly yyyy-MM-ddTHH:mm:ss.fffZ, and a time that the calendar holds.
    public static DateTimeOffset ReadTime(JsonElement message, string name)
    {
        string text = ReadString(message, name);

        if (!OutpostForms.TryReadTime(text, out DateTimeOffset time))
        {
            throw Form($"The member {name} is not a time in the form yyyy-MM-ddTHH:mm:ss.fffZ.");
        }

        return time;
    }

    public static Guid ReadId(JsonElement message, string name)
    {
        if (!TryReadId(message.GetProperty(name), out Guid id))
        {
            throw Form($"The member {name} is not an identity in the form of 36 lowercase characters.");
        }

        return id;
    }

    public static byte[] ReadBytes(JsonElement message, string name)
    {
        if (!TryReadBytes(message.GetProperty(name), out byte[]? bytes))
        {
            throw Form($"The member {name} is not canonical base64url without padding.");
        }

        return bytes;
    }

    public static bool ReadBoolean(JsonElement message, string name)
    {
        return message.GetProperty(name).ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Form($"The member {name} is not true or false."),
        };
    }

    public static JsonElement ReadArray(JsonElement message, string name)
    {
        JsonElement value = message.GetProperty(name);

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Form($"The member {name} is not an array.");
        }

        return value;
    }

    public static List<string> ReadStrings(JsonElement message, string name)
    {
        List<string> strings = [];

        foreach (JsonElement item in ReadArray(message, name).EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw Form($"An item of {name} is not a string.");
            }

            strings.Add(item.GetString()!);
        }

        return strings;
    }

    public static List<Guid> ReadIds(JsonElement message, string name)
    {
        List<Guid> ids = [];

        foreach (JsonElement item in ReadArray(message, name).EnumerateArray())
        {
            if (!TryReadId(item, out Guid id))
            {
                throw Form($"An item of {name} is not an identity in the form of 36 lowercase characters.");
            }

            ids.Add(id);
        }

        return ids;
    }

    // The text of a string, or null for one that holds bytes that are not UTF-8 or a lone surrogate, escaped or not,
    // which GetString refuses with InvalidOperationException.
    private static string? TextOf(JsonElement value)
    {
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // The text of a name, refused as TextOf refuses the text of a string.
    private static string? NameOf(JsonProperty member)
    {
        try
        {
            return member.Name;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string CheckedText(string? text)
    {
        if (text is null)
        {
            throw Form("A name or a string holds bytes that are not UTF-8, or a lone surrogate.");
        }

        if (text.Contains('\0'))
        {
            throw Form("A name or a string holds U+0000.");
        }

        return text;
    }
}
