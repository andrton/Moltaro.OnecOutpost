using Moltaro.OnecOutpost.OneC.Tests.Workers;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.OneC.Tests.Messages;

// The executor replaces each U+0000 and each lone surrogate in its messages and its error text with U+FFFD, then trims
// them, never inside a surrogate pair, before it writes and measures its outcome object. The object crosses COM as
// UTF-16 and the worker encodes it as UTF-8, so the size the executor measured is the length of the bytes the agent
// receives (the brief of S9, "Contract", and §8 of docs/protocol.md). RunAsync holds every outcome to that size.
public sealed class TextCleaningTests
{
    private const string Pair = "\U0001F600";

    // Two messages and an error, each with U+0000 or a lone surrogate between two letters: Char(0), the high surrogate
    // Char(55357) and the low surrogate Char(56832), which together are U+1F600.
    private const string MessagesAndAnErrorWithU0000AndLoneSurrogates = """
        Message("A" + Char(0) + "B");
        Message("C" + Char(55357) + "D");
        Raise "E" + Char(0) + "F" + Char(56832) + "G";
        """;

    // A text of 2000 characters outside the Basic Multilingual Plane, 8000 bytes of UTF-8, followed by the statement
    // that hands it over as a message or as the text of an error.
    private const string TwoThousandPairs = """
        Text = "";
        For Number = 1 To 2000 Do
            Text = Text + Char(55357) + Char(56832);
        EndDo;

        """;

    // The statement, and the kind and code of the outcome it gives.
    public static TheoryData<string, string, string?> TextsTrimmedAtAPair => new()
    {
        { "Message(Text);", "succeeded", null },
        { "Raise Text;", "failed", "script-error" },
    };

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task U0000_and_a_lone_surrogate_in_the_messages_and_the_error_text_arrive_as_UFFFD_where_they_were()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        OutcomeLine read = await reader.RunAsync(MessagesAndAnErrorWithU0000AndLoneSurrogates);

        read.AssertIs("failed", "script-error");
        Assert.Equal(["A�B", "C�D"], read.Messages);
        Assert.Contains("E�F�G", read.ErrorText);
        Assert.False(read.MessagesTrimmed);
        Assert.Equal(read.OutcomeBytes.Length, read.Size);
    }

    // The limits 1024 to 1027 move the point of trimming over the four bytes of a pair, so that one of them falls
    // inside a pair whatever the executor counts.
    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(TextsTrimmedAtAPair))]
    public async Task A_text_trimmed_at_a_character_outside_the_Basic_Multilingual_Plane_keeps_the_pair_whole(
        string statement,
        string kind,
        string? code)
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        for (long limit = 1024; limit <= 1027; limit++)
        {
            OutcomeLine read = await reader.RunAsync(TwoThousandPairs + statement, outcomeLimit: limit);

            read.AssertIs(kind, code);
            Assert.True(read.MessagesTrimmed, read.ToString());
            Assert.InRange(read.Size, 1, limit);
            Assert.Equal(read.OutcomeBytes.Length, read.Size);

            string text = code is null ? string.Concat(read.Messages) : read.ErrorText!;
            Assert.InRange(CountPairs(text), 1, 1999);
            Assert.DoesNotContain("�", text);
            Assert.False(HasALoneSurrogate(text), read.ToString());
        }
    }

    private static int CountPairs(string text)
    {
        int count = 0;
        int index = text.IndexOf(Pair, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(Pair, index + Pair.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static bool HasALoneSurrogate(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(text[index]))
            {
                return true;
            }
        }

        return false;
    }
}
