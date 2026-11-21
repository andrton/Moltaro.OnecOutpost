using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Protocol.Messages;

// The outcome object (§8 of docs/protocol.md), with the members of its JSON. Code is null for `succeeded`. Result is
// a JsonElement for `succeeded`, of the kind Null when the result is `null`, and a C# null for every other kind.
// ErrorText is null but for `failed` with `script-error`.
internal sealed record OutpostOutcomeMessage(
    OutpostAgentOutcomeKindEnum Kind,
    string? Code,
    JsonElement? Result,
    string? ErrorText,
    IReadOnlyList<string> Messages,
    bool MessagesTrimmed);
