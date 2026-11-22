namespace Moltaro.OnecOutpost.OneC.Tests.Guides;

// One script of a guide, as its fenced block gives it: the line of the block's fence, counted from 1, and the heading
// it is under, which together name it; its mode, read or write; the kind and the code it gives on the test base,
// succeeded and null unless its info string names others; its text; and the text of its parameters, {} without a
// block of them.
internal sealed record GuideScript(int Line, string Heading, string Mode, string Kind, string? Code, string Script, string Parameters)
{
    public string Name => $"line {Line}, {Heading}";
}
