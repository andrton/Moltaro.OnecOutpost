using System.Text.Json;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// What the session of one 1C user of a base does in a COM worker that check or metadata started on a test's machine, as
// the test scripts it. Its open gives {"ready":true} until the test refuses it, every open or those from one on, which
// ends the worker with the diagnostics the test gives, or makes it never answer until the worker is killed. Each script
// of check, by its kind (FakeCheckProcess), gives the executor's outcome of its kind: until the test gives another, the
// user's own name and full name for user, no subscription whose common module an external connection lacks for
// subscriptions, and no right to change data for rights; a script that overruns gives nothing until the worker is
// killed. The script of metadata, the walk, is answered by the configuration of the base that the user sees, which every
// session of the user shares.
internal sealed class FakeSession
{
    private readonly Dictionary<string, byte[]?> _outcomes = new(StringComparer.Ordinal);

    // The opens so far, and the first one that is refused, from which on every one is.
    private int _opens;

    private int? _refusedFrom;

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

    // The metadata of the base as this user sees it, which answers the walk of metadata; null for a base that the test
    // walks not.
    public FakeConfiguration? Configuration { get; init; }

    // No session opens: the worker answers {"ready":false} and ends, its standard error holding the diagnostics.
    public void RefuseOpen(string diagnostics)
    {
        Refuses = true;
        Diagnostics = diagnostics;
    }

    // The opens before the one given, counted from 1, give a session, and that one and every later one are refused as
    // RefuseOpen refuses them, such as the open of a session that replaces a worker that ended.
    public void RefuseOpenFrom(int open, string diagnostics)
    {
        _refusedFrom = open;
        Diagnostics = diagnostics;
    }

    // Counts an open, and whether it is refused.
    public bool RefusesOpen()
    {
        _opens++;

        return Refuses || _opens >= _refusedFrom;
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

    // The executor's outcome of the script of the kind with its parameters, or null for one that overruns: the walk's by
    // the configuration of the base, the others by their kind.
    public byte[]? OutcomeOf(string script, string parametersJson)
    {
        if (script == FakeCheckProcess.WalkScript && Configuration is not null)
        {
            return Configuration.OutcomeOf(parametersJson);
        }

        return OutcomeOf(script);
    }

    // The executor's outcome of the script of the kind, or null for one that overruns.
    private byte[]? OutcomeOf(string script)
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
