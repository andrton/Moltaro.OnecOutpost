using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Audit;

// What the audit log knows of one command: its mode from its received or refused, null when neither names one; whether
// it was received; the kind and the code of its last ended or refused, in the order of file names and lines, with the
// durationMs of that ended; and whether it was confirmed.
internal sealed record AuditCommand(
    Guid Id,
    OutpostModeEnum? Mode,
    bool Received,
    OutpostAgentOutcomeKindEnum? Kind,
    string? Code,
    long? DurationMs,
    bool Confirmed);
