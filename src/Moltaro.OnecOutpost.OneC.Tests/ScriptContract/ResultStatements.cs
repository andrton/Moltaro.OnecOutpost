namespace Moltaro.OnecOutpost.OneC.Tests.ScriptContract;

// Statements that set Result to a value the executor refuses before it writes it, since no string of the protocol holds
// U+0000 or a lone surrogate, and a result nests at most 64 levels (§2 and §8 of docs/protocol.md). Char(55357) is a
// high surrogate without its low one.
internal static class ResultStatements
{
    public const string HoldingU0000 = "Result = \"A\" + Char(0) + \"B\";";

    public const string HoldingALoneSurrogate = "Result = \"A\" + Char(55357) + \"B\";";

    // Arrays nested the given number of levels, the outer array being level 1 and the innermost empty.
    public static string NestedArrays(int levels)
    {
        return $"""
            Result = New Array;
            Inner = Result;
            For Level = 2 To {levels} Do
                Nested = New Array;
                Inner.Add(Nested);
                Inner = Nested;
            EndDo;
            """;
    }
}
