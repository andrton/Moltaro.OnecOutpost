using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Checks;

// The verdict on one command, with what the loop records of it: the identity, the base, the mode and the code that a
// Refused records, as CommandChecks.Check says which check gives which (no identity for signature and form); and the
// command read, for Runs.
internal sealed record CommandCheck(
    CommandVerdictEnum Verdict,
    Guid? Id,
    string? BaseKey,
    OutpostModeEnum? Mode,
    string? Code,
    OutpostCommandMessage? Command);
