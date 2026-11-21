namespace Moltaro.OnecOutpost.Protocol.Outcomes;

// The mode of a command, `read` or `write` (§7 of docs/protocol.md). It decides which outcomes an agent may send
// (§8), so it lives with the outcome codes.
internal enum OutpostModeEnum
{
    Read,
    Write,
}
