using System.Text.Json;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;

namespace Moltaro.OnecOutpost.Agent.Tests.RunningAgent;

// The changes and operations of a data folder in memory as words, so that a test of the loop states their order: an
// entry of the audit log as its kind, with its command when it names one, such as "received {id}" or "connection";
// every other change, operation of a fake worker and request to the fake server as its line, such as
// "replace outcomes\{id}.json", "run accounting read 1" or "request outcome".
internal static class Operations
{
    private static readonly JsonDocumentOptions MessageDepth = new() { MaxDepth = OutpostProtocol.MaxMessageDepth };

    public static List<string> Since(MemoryDataFolder data, int count)
    {
        return [.. data.Changes.Skip(count).Select(Word)];
    }

    // The word of one change or operation; an append that failed, torn, is its line. It asserts nothing of the form of
    // an entry, which the tests of the audit log state, so that it can match a change while the agent makes it
    // (MemoryDataFolder.WaitForAsync).
    public static string Word(FolderChange change)
    {
        bool whole = change.Bytes.Length > 0 && change.Bytes[^1] == (byte)'\n';

        if (!change.Line.StartsWith(@"append audit\", StringComparison.Ordinal) || !whole)
        {
            return change.Line;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(change.Bytes.AsMemory(0, change.Bytes.Length - 1), MessageDepth);
            JsonElement entry = document.RootElement;
            string? kind = entry.TryGetProperty("entry", out JsonElement value) ? value.GetString() : null;
            bool named = entry.TryGetProperty("command", out JsonElement command) && command.ValueKind == JsonValueKind.String;

            return named ? $"{kind} {command.GetString()}" : kind ?? change.Line;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return change.Line;
        }
    }

    // A match of the change or operation with this word, for MemoryDataFolder.WaitForAsync.
    public static Func<FolderChange, bool> Is(string word)
    {
        return change => Word(change) == word;
    }

    // The operations hold these words in this order, with any others between them.
    public static void AssertInOrder(IReadOnlyList<string> operations, params string[] expected)
    {
        int from = 0;

        foreach (string word in expected)
        {
            int found = -1;

            for (int index = from; index < operations.Count; index++)
            {
                if (operations[index] == word)
                {
                    found = index;
                    break;
                }
            }

            Assert.True(found >= 0, $"\"{word}\" does not follow in: {string.Join(" | ", operations)}");
            from = found + 1;
        }
    }

    // The path of a command's outbox file in the data folder, as a change names it.
    public static string OutboxFile(Guid command)
    {
        return $@"outcomes\{command}.json";
    }
}
