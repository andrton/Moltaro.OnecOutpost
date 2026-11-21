using System.Text;

namespace Moltaro.OnecOutpost.Protocol.Tests;

// The text of a JSON object that a test writes by hand: its members in order, each with its value as raw JSON. The
// tests of the readers start from a body that keeps every rule and change one member of it, since a correct writer
// can never produce the bodies that a reader must refuse. Nothing here uses the codec.
internal sealed class MessageText
{
    private readonly List<(string Name, string Value)> _members;

    public MessageText(params (string Name, string Value)[] members)
    {
        _members = [.. members];
    }

    // The same object with the member set to the value: in its place when the object has it, at the end otherwise.
    public MessageText With(string name, string value)
    {
        MessageText changed = new([.. _members]);
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

    // The same object with one more member of the name, whether it has one already or not.
    public MessageText WithAnother(string name, string value)
    {
        MessageText changed = new([.. _members]);
        changed._members.Add((name, value));

        return changed;
    }

    public MessageText Without(string name)
    {
        MessageText changed = new([.. _members]);
        changed._members.RemoveAll(member => member.Name == name);

        return changed;
    }

    // The same object with its members in the opposite order.
    public MessageText Reversed()
    {
        MessageText changed = new([.. _members]);
        changed._members.Reverse();

        return changed;
    }

    public override string ToString()
    {
        return "{" + string.Join(",", _members.Select(member => Quote(member.Name) + ":" + member.Value)) + "}";
    }

    public byte[] ToBytes()
    {
        return Utf8(ToString());
    }

    public static byte[] Utf8(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    // The text in quotes, as it is: the caller writes the escapes it wants.
    public static string Quote(string text)
    {
        return "\"" + text + "\"";
    }

    public static string Array(IEnumerable<string> values)
    {
        return "[" + string.Join(",", values) + "]";
    }

    // Base64url without padding (RFC 4648 §5), made without the codec.
    public static string Base64Url(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // A time in the one form of §2, made without the codec.
    public static string Time(DateTimeOffset time)
    {
        return Quote(time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
    }

    // An object nested the given number of levels, the outer object being level 1: each object but the innermost,
    // which is empty, holds the next under the name "level".
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
