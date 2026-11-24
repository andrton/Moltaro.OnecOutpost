using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.TestSupport.FakeWorkers;

// Outcome objects (§8 of docs/protocol.md) as the outbox holds them.
internal static class TestOutcomes
{
    // An outcome that the agent writes itself: OutpostCodec.Write of one with no result, no error text and no messages.
    public static byte[] Own(OutpostAgentOutcomeKindEnum kind, string code)
    {
        return OutpostCodec.Write(new OutpostOutcomeMessage(kind, code, null, null, [], false));
    }

    // The kind of its word in an outcome object and in ended, such as unknown: a theory names it so, since the
    // protocol's types are internal.
    public static OutpostAgentOutcomeKindEnum KindOf(string word)
    {
        return Enum.Parse<OutpostAgentOutcomeKindEnum>(word, ignoreCase: true);
    }

    // Outcomes of the executor in its own form, with the white space that the writer of §2 does not write: the agent
    // forwards their bytes as they are (§8).
    public static byte[] Succeeded()
    {
        return """{ "kind": "succeeded", "code": null, "result": { "rows": 3 }, "errorText": null, "messages": [ "Done" ], "messagesTrimmed": false }"""u8.ToArray();
    }

    public static byte[] ScriptError()
    {
        return """{ "kind": "failed", "code": "script-error", "result": null, "errorText": "Division by zero", "messages": [], "messagesTrimmed": false }"""u8.ToArray();
    }

    public static byte[] TransactionEnded()
    {
        return """{ "kind": "unknown", "code": "transaction-ended", "result": null, "errorText": null, "messages": [], "messagesTrimmed": false }"""u8.ToArray();
    }
}
