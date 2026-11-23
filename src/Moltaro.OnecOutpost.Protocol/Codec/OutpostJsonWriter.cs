using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Moltaro.OnecOutpost.Protocol.Codec;

// The writer of §2 of docs/protocol.md, which every writer of the codec uses: UTF-8 without a byte order mark, no white
// space between tokens, and in a string only the escapes of §2. Utf8JsonWriter is not used, since its encoders escape
// more than §2 allows, such as U+007F, U+2028 and every character beyond the BMP.
//
// It keeps the bytes it writes, or, made by Counter, only counts them: CommandSize counts with the very methods that
// Write(OutpostCommandMessage) writes with, so that the size and the bytes of a command can never disagree. The
// members and items of one object or array are separated by commas as they are written; the caller writes a name
// before each value of an object.
internal sealed class OutpostJsonWriter
{
    // A lone surrogate is a caller's mistake that UTF-8 cannot carry: it is refused, never written as U+FFFD.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Null for a writer that only counts.
    private readonly ArrayBufferWriter<byte>? _bytes;

    private long _length;

    // True after a value, when a comma comes before the next name or item of the same object or array.
    private bool _afterValue;

    public OutpostJsonWriter()
        : this(new ArrayBufferWriter<byte>())
    {
    }

    private OutpostJsonWriter(ArrayBufferWriter<byte>? bytes)
    {
        _bytes = bytes;
    }

    // The bytes written so far, or counted so far by a writer that only counts.
    public long Length => _length;

    // A writer that keeps no bytes and only counts them.
    public static OutpostJsonWriter Counter()
    {
        return new OutpostJsonWriter(null);
    }

    public byte[] ToArray()
    {
        return _bytes!.WrittenSpan.ToArray();
    }

    public void StartObject()
    {
        StartValue();
        Append("{"u8);
        _afterValue = false;
    }

    public void EndObject()
    {
        Append("}"u8);
        _afterValue = true;
    }

    public void StartArray()
    {
        StartValue();
        Append("["u8);
        _afterValue = false;
    }

    public void EndArray()
    {
        Append("]"u8);
        _afterValue = true;
    }

    public void Name(string name)
    {
        StartValue();
        WriteQuoted(name);
        Append(":"u8);
        _afterValue = false;
    }

    public void String(string text)
    {
        StartValue();
        WriteQuoted(text);
        _afterValue = true;
    }

    public void NullableString(string? text)
    {
        if (text is null)
        {
            Null();
        }
        else
        {
            String(text);
        }
    }

    // An array of strings, in their order.
    public void Strings(IReadOnlyList<string> strings)
    {
        StartArray();

        foreach (string text in strings)
        {
            String(text);
        }

        EndArray();
    }

    public void Integer(long value)
    {
        StartValue();
        AppendText(value.ToString(CultureInfo.InvariantCulture));
        _afterValue = true;
    }

    public void NullableInteger(long? value)
    {
        if (value is null)
        {
            Null();
        }
        else
        {
            Integer(value.Value);
        }
    }

    public void Boolean(bool value)
    {
        StartValue();
        Append(value ? "true"u8 : "false"u8);
        _afterValue = true;
    }

    public void Null()
    {
        StartValue();
        Append("null"u8);
        _afterValue = true;
    }

    // A time of §2: UTC, as yyyy-MM-ddTHH:mm:ss.fffZ. The format truncates anything finer than a millisecond.
    public void Time(DateTimeOffset time)
    {
        String(time.UtcDateTime.ToString(OutpostJsonReader.TimeFormat, CultureInfo.InvariantCulture));
    }

    // A duration of §2: whole milliseconds, truncated.
    public void Duration(TimeSpan duration)
    {
        Integer(duration.Ticks / TimeSpan.TicksPerMillisecond);
    }

    public void Id(Guid id)
    {
        String(OutpostForms.WriteId(id));
    }

    public void Bytes(ReadOnlySpan<byte> bytes)
    {
        String(OutpostForms.WriteBase64Url(bytes));
    }

    // A JSON value written again by §2, whatever form it was read from: white space dropped, every name and string
    // escaped as §2 says. A number is written as the text it was read from.
    public void Value(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                StartObject();

                foreach (JsonProperty member in value.EnumerateObject())
                {
                    Name(member.Name);
                    Value(member.Value);
                }

                EndObject();
                break;

            case JsonValueKind.Array:
                StartArray();

                foreach (JsonElement item in value.EnumerateArray())
                {
                    Value(item);
                }

                EndArray();
                break;

            case JsonValueKind.String:
                String(value.GetString()!);
                break;

            default:
                // A number, true, false or null, in ASCII.
                StartValue();
                AppendText(value.GetRawText());
                _afterValue = true;
                break;
        }
    }

    // A JSON value given as its bytes, which are written as they are: the outcome object of the executor keeps its own
    // form (§8).
    public void Raw(ReadOnlySpan<byte> json)
    {
        StartValue();
        Append(json);
        _afterValue = true;
    }

    // The escapes of §2 for one UTF-16 code unit, or null for a code unit written as it is. Every character that needs
    // an escape is ASCII, so no escape falls inside a surrogate pair.
    private static string? Escape(char character)
    {
        return character switch
        {
            '"' => "\\\"",
            '\\' => "\\\\",
            '\b' => "\\b",
            '\t' => "\\t",
            '\n' => "\\n",
            '\f' => "\\f",
            '\r' => "\\r",

            // Every other control character as the six characters of the escape with four hexadecimal digits, the
            // last two uppercase.
            < ' ' => "\\" + "u00" + ((int)character).ToString("X2", CultureInfo.InvariantCulture),

            _ => null,
        };
    }

    private void StartValue()
    {
        if (_afterValue)
        {
            Append(","u8);
        }
    }

    private void WriteQuoted(string text)
    {
        Append("\""u8);

        // The start of the text that is not written yet: the runs between two escapes are written whole.
        int start = 0;

        for (int index = 0; index < text.Length; index++)
        {
            string? escape = Escape(text[index]);

            if (escape is not null)
            {
                AppendText(text.AsSpan(start, index - start));
                AppendText(escape);
                start = index + 1;
            }
        }

        AppendText(text.AsSpan(start));
        Append("\""u8);
    }

    private void AppendText(ReadOnlySpan<char> text)
    {
        int count = StrictUtf8.GetByteCount(text);

        if (_bytes is not null)
        {
            StrictUtf8.GetBytes(text, _bytes.GetSpan(count));
            _bytes.Advance(count);
        }

        _length += count;
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        _bytes?.Write(bytes);
        _length += bytes.Length;
    }
}
