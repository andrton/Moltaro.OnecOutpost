namespace Moltaro.OnecOutpost.Agent.Transport;

// What the answer to an outcome request gives (§9 of docs/protocol.md), judged once: the result that the loop acts on;
// the time of a verified confirmation or of a verified refusal; the HTTP status when one came; for any answer but a
// confirmation, the cause, and for a verified refusal its code.
internal sealed record OutcomeAnswer(
    OutcomeResultEnum Result,
    DateTimeOffset? Time,
    int? Status,
    ServerAnswerCauseEnum? Cause,
    string? Code);
