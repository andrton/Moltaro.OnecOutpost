using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// Outcome objects as an agent sends them (§8 of docs/protocol.md), for the tests of commands. Which ones an agent may
// send for which mode is the subject of the codec's tests; these tests send only those it may.
internal static class AgentOutcomes
{
    // A result given as JSON text, with one user message of 1C.
    public static OutpostOutcomeMessage Succeeded(string result, bool messagesTrimmed = false)
    {
        using JsonDocument parsed = JsonDocument.Parse(result);

        return new OutpostOutcomeMessage(
            OutpostAgentOutcomeKindEnum.Succeeded,
            null,
            parsed.RootElement.Clone(),
            null,
            ["Posted"],
            messagesTrimmed);
    }

    // A script error, the one failure with the error text of 1C, and one user message.
    public static OutpostOutcomeMessage ScriptError(string errorText)
    {
        return new OutpostOutcomeMessage(
            OutpostAgentOutcomeKindEnum.Failed,
            OutpostOutcomeCodes.ScriptError,
            null,
            errorText,
            ["The field is not filled in"],
            false);
    }

    public static OutpostOutcomeMessage Refused(string code)
    {
        return new OutpostOutcomeMessage(OutpostAgentOutcomeKindEnum.Refused, code, null, null, [], false);
    }

    public static OutpostOutcomeMessage Unknown(string code)
    {
        return new OutpostOutcomeMessage(OutpostAgentOutcomeKindEnum.Unknown, code, null, null, [], false);
    }
}
