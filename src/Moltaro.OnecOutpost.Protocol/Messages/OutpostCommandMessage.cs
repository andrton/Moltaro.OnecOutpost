using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Protocol.Messages;

// A command (§7 of docs/protocol.md), with the members of its JSON: the protocol version, its identity, the agent it
// is for, the base and the mode, the caller key, null for a Read without one, the script, the parameters, an object
// that is `{}` when there are none, the time it was issued, its take deadline, and the server's ClockSkew and
// OutcomeSizeLimit when it was made.
internal sealed record OutpostCommandMessage(
    int Protocol,
    Guid Id,
    Guid Agent,
    string Base,
    OutpostModeEnum Mode,
    string? CallerKey,
    string Script,
    JsonElement Parameters,
    DateTimeOffset Issued,
    DateTimeOffset Deadline,
    TimeSpan Skew,
    int OutcomeLimit);
