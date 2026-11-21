namespace Moltaro.OnecOutpost.Protocol;

// The requests of an agent (§1 of docs/protocol.md). A signing input names the kind as the word `pair`, `poll` or
// `outcome` (§3), so that a body signed as one kind of request is never taken for another.
internal enum OutpostRequestKindEnum
{
    Pair,
    Poll,
    Outcome,
}
