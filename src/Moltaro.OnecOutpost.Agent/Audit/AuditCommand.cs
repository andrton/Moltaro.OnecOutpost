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
    bool Confirmed)
{
    // The facts of one entry, for its writer and its reader alike: a received gives the mode; a refused the mode where
    // it is known, the kind Refused and the code; an ended the kind, the code and the duration; a confirmed that it
    // was confirmed.
    public static AuditCommand OfReceived(Guid id, OutpostModeEnum mode)
    {
        return new AuditCommand(id, mode, true, null, null, null, false);
    }

    public static AuditCommand OfRefused(Guid id, OutpostModeEnum? mode, string code)
    {
        return new AuditCommand(id, mode, false, OutpostAgentOutcomeKindEnum.Refused, code, null, false);
    }

    public static AuditCommand OfEnded(Guid id, OutpostAgentOutcomeKindEnum kind, string? code, long? durationMs)
    {
        return new AuditCommand(id, null, false, kind, code, durationMs, false);
    }

    public static AuditCommand OfConfirmed(Guid id)
    {
        return new AuditCommand(id, null, false, null, null, null, true);
    }
}
