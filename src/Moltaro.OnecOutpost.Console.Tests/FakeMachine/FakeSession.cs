using System.Text.Json;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// What the session of one 1C user of a base does in a COM worker that check started on a test's machine, as the test
// scripts it. Its open gives {"ready":true} until the test refuses it, which ends the worker with the diagnostics the
// test gives, or makes it never answer until the worker is killed. Each script of check, by its kind (FakeCheckProcess),
// gives the executor's outcome of its kind: until the test gives another, the user's own name and full name for user, no
// subscription whose common module an external connection lacks for subscriptions, and no right to change data for
// rights; a script that overruns gives nothing until the worker is killed.
internal sealed class FakeSession
{
    private readonly Dictionary<string, byte[]?> _outcomes = new(StringComparer.Ordinal);

    public FakeSession(string user, string fullName)
    {
        Gives(FakeCheckProcess.UserScript, new JsonObject { ["user"] = user, ["fullName"] = fullName });
        Gives(FakeCheckProcess.SubscriptionsScript, new JsonArray());
        Gives(FakeCheckProcess.RightsScript, new JsonObject { ["global"] = new JsonArray(), ["categories"] = new JsonArray() });
    }

    // The open gives {"ready":false}, and the worker ends with these diagnostics on its standard error.
    public bool Refuses { get; private set; }

    // The open gives nothing until the worker is killed.
    public bool Hangs { get; private set; }

    // What the worker writes to its standard error before it ends.
    public string Diagnostics { get; private set; } = "";

    // No session opens: the worker answers {"ready":false} and ends, its standard error holding the diagnostics.
    public void RefuseOpen(string diagnostics)
    {
        Refuses = true;
        Diagnostics = diagnostics;
    }

    // The worker never answers its connection line.
    public void HangOpen()
    {
        Hangs = true;
    }

    // The script of the kind succeeds with the result.
    public void Gives(string script, JsonNode result)
    {
        using JsonDocument document = JsonDocument.Parse(result.ToJsonString());

        _outcomes[script] = OutpostCodec.Write(new OutpostOutcomeMessage(
            OutpostAgentOutcomeKindEnum.Succeeded,
            null,
            document.RootElement.Clone(),
            null,
            [],
            false));
    }

    // The script of the kind fails with a script error and the text of 1C.
    public void Fails(string script, string errorText)
    {
        _outcomes[script] = OutpostCodec.Write(new OutpostOutcomeMessage(
            OutpostAgentOutcomeKindEnum.Failed,
            OutpostOutcomeCodes.ScriptError,
            null,
            errorText,
            [],
            false));
    }

    // The script of the kind runs past any run limit.
    public void Overruns(string script)
    {
        _outcomes[script] = null;
    }

    // The executor's outcome of the script of the kind, or null for one that overruns.
    public byte[]? OutcomeOf(string script)
    {
        return _outcomes.TryGetValue(script, out byte[]? outcome)
            ? outcome
            : OutpostCodec.Write(new OutpostOutcomeMessage(
                OutpostAgentOutcomeKindEnum.Failed,
                OutpostOutcomeCodes.ScriptError,
                null,
                $"The fake session runs no script of the kind {script}.",
                [],
                false));
    }
}
