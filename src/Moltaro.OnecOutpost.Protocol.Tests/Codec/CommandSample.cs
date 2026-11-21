namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The command that §7 of docs/protocol.md shows, a Write, as the text of its bytes. The tests of the reader of commands
// start from it.
internal static class CommandSample
{
    public static MessageText Text => new(
        ("protocol", "1"),
        ("id", MessageText.Quote(Sample.CommandText)),
        ("agent", MessageText.Quote(Sample.AgentText)),
        ("base", "\"accounting\""),
        ("mode", "\"write\""),
        ("callerKey", "\"invoice-2026-11-20-17\""),
        ("script", "\"Result = Parameters[\\\"date\\\"];\""),
        ("parameters", "{\"date\":\"2026-11-20\"}"),
        ("issued", "\"2026-11-20T10:15:00.000Z\""),
        ("deadline", "\"2026-11-20T10:15:30.000Z\""),
        ("skewMs", "120000"),
        ("outcomeLimit", "16777216"));
}
