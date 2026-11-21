namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The bodies that §6, §8 and §9 of docs/protocol.md show, as text. The tests of the readers start from them.
internal static class BodySample
{
    // The outcome object of a command that succeeded (§6.3).
    public const string Outcome =
        """{"kind":"succeeded","code":null,"result":{"rows":3},"errorText":null,"messages":[],"messagesTrimmed":false}""";

    // The body of a `poll` (§6.2): two bases, the second without writing, with two free workers in all.
    public static MessageText Poll => new(
        ("number", "639307665001230000"),
        ("time", "\"2026-11-20T10:15:00.123Z\""),
        ("agentVersion", "\"0.1.0\""),
        ("pollTimeoutMs", "25000"),
        (
            "bases",
            """[{"key":"accounting","read":{"free":1,"runLimitMs":600000},"write":{"free":0,"runLimitMs":600000}},{"key":"warehouse","read":{"free":1,"runLimitMs":600000},"write":null}]"""),
        ("received", MessageText.Array([MessageText.Quote(Sample.CommandText)])));

    // The body of the answer to a `poll` (§6.2), without commands.
    public static MessageText PollAnswer => new(
        ("time", "\"2026-11-20T10:15:01.456Z\""),
        ("newest", "null"),
        ("pollTimeoutMs", "25000"),
        ("leaseGraceMs", "10000"),
        ("bases", """{"held":["accounting"],"claimed":[]}"""),
        ("commands", "[]"));

    // The body of an `outcome` (§6.3).
    public static MessageText OutcomeRequest => new(
        ("command", MessageText.Quote(Sample.CommandText)),
        ("outcome", Outcome));

    // The body of the answer to an `outcome` (§6.3).
    public static MessageText OutcomeAnswer => new(
        ("time", "\"2026-11-20T10:15:02.789Z\""),
        ("confirmed", MessageText.Quote(Sample.CommandText)));

    // The body of a signed refusal (§9).
    public static MessageText Refusal => new(
        ("time", "\"2026-11-20T10:15:00.123Z\""),
        ("code", "\"form\""));
}
