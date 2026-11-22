using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Tests.FakeFiles;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;

namespace Moltaro.OnecOutpost.Agent.Tests;

// The entries of the audit log as the agent wrote them, read back by the test. An entry is parsed with the depth of a
// message (§2 of docs/protocol.md), as the log reads it.
internal static class AuditEntries
{
    private static readonly JsonDocumentOptions MessageDepth = new() { MaxDepth = OutpostProtocol.MaxMessageDepth };

    // The entries that the changes since the list held `count` changes appended to audit\, in their order. Each
    // append is asserted to be one whole entry.
    public static List<JsonElement> AppendedSince(MemoryDataFolder data, int count)
    {
        List<JsonElement> entries = [];

        foreach (FolderChange change in data.Changes.Skip(count))
        {
            if (change.Line.StartsWith("append audit\\", StringComparison.Ordinal))
            {
                entries.Add(WholeEntry(change.Bytes));
            }
        }

        return entries;
    }

    // The kinds of the entries appended since the list held `count` changes, such as started and policy.
    public static List<string> KindsSince(MemoryDataFolder data, int count)
    {
        return [.. AppendedSince(data, count).Select(Kind)];
    }

    // One entry as one append writes it: one JSON object by the writer of §2, which writes no white space between
    // tokens and escapes only what §2 escapes, and LF last.
    public static JsonElement WholeEntry(byte[] line)
    {
        Assert.True(line.Length > 0 && line[^1] == (byte)'\n', "An entry ends with LF.");

        byte[] body = line[..^1];
        Assert.DoesNotContain((byte)'\n', body);

        using JsonDocument document = JsonDocument.Parse(body, MessageDepth);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);

        OutpostJsonWriter writer = new();
        writer.Value(document.RootElement);
        Assert.Equal(writer.ToArray(), body);

        return document.RootElement.Clone();
    }

    public static string Kind(JsonElement entry)
    {
        return entry.GetProperty("entry").GetString()!;
    }

    // A member of an entry that the table gives only when there is one, such as the error of started: absent, or null.
    public static void AssertNone(JsonElement entry, string name)
    {
        if (entry.TryGetProperty(name, out JsonElement value))
        {
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
        }
    }

    // The entry holds no member but these.
    public static void AssertOnly(JsonElement entry, params string[] names)
    {
        foreach (JsonProperty member in entry.EnumerateObject())
        {
            Assert.Contains(member.Name, names);
        }
    }

    public static List<string?> Strings(JsonElement array)
    {
        return [.. array.EnumerateArray().Select(item => item.GetString())];
    }
}
