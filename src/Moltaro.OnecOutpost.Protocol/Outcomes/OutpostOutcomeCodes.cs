namespace Moltaro.OnecOutpost.Protocol.Outcomes;

// The one place of every outcome code and of which code fits which kind. The outcome object of §8 of docs/protocol.md
// carries these strings in its `code`. The public OutpostCodes of the server defines each of its constants as the
// constant here, and documents what each code means.
internal static class OutpostOutcomeCodes
{
    public const string ScriptError = "script-error";

    public const string ResultType = "result-type";

    public const string ResultTooLarge = "result-too-large";

    public const string RunLimit = "run-limit";

    public const string Lost = "lost";

    public const string CallerKeyConflict = "caller-key-conflict";

    public const string UnknownBase = "unknown-base";

    public const string ModeClosed = "mode-closed";

    public const string NotServed = "not-served";

    public const string NotAddressed = "not-addressed";

    public const string ProtocolVersion = "protocol-version";

    public const string Malformed = "malformed";

    public const string Expired = "expired";

    public const string ClockSkew = "clock-skew";

    public const string Revoked = "revoked";

    public const string Disconnected = "disconnected";

    public const string AgentStopped = "agent-stopped";

    public const string TransactionEnded = "transaction-ended";

    public const string BaseUnreachable = "base-unreachable";

    public const string ConnectionLost = "connection-lost";

    // The three Fits answer the kinds column of the table of OutpostCodes: whether a code is one that an outcome of
    // the kind carries, whoever set it, an agent (§8) or the server. A code that belongs to two kinds fits both, since
    // only the mode of the command, which an outcome does not hold, decides between them. The public factories of
    // OutpostOutcome use them, so that the fit of a code to a kind lives once. They throw nothing.
    public static bool FitsFailed(string code)
    {
        return code is ScriptError or ResultType or ResultTooLarge or RunLimit or Lost;
    }

    public static bool FitsRefused(string code)
    {
        return code
            is CallerKeyConflict
            or UnknownBase
            or ModeClosed
            or NotServed
            or NotAddressed
            or ProtocolVersion
            or Malformed
            or Expired
            or ClockSkew
            or Revoked;
    }

    public static bool FitsUnknown(string code)
    {
        return code
            is ResultType
            or ResultTooLarge
            or RunLimit
            or Revoked
            or Disconnected
            or AgentStopped
            or TransactionEnded;
    }

    // Answers the table "The outcomes an agent may send, by mode" of §8: true when an agent may send an outcome of
    // this kind with this code for a command of this mode. The code is null for `succeeded`, and only for it. Every
    // other combination is the server's, and gives false.
    public static bool IsAgentOutcome(OutpostModeEnum mode, OutpostAgentOutcomeKindEnum kind, string? code)
    {
        return kind switch
        {
            OutpostAgentOutcomeKindEnum.Succeeded => code is null,

            // A Write fails only after 1C rolled back, so only these codes can say so.
            OutpostAgentOutcomeKindEnum.Failed when mode == OutpostModeEnum.Write =>
                code is ScriptError or ResultType or ResultTooLarge,

            OutpostAgentOutcomeKindEnum.Failed => code is ScriptError or ResultType or ResultTooLarge or RunLimit or Lost,

            OutpostAgentOutcomeKindEnum.Refused => code
                is NotServed
                or NotAddressed
                or ProtocolVersion
                or Malformed
                or Expired
                or ClockSkew,

            // Only a Write ends unknown.
            OutpostAgentOutcomeKindEnum.Unknown when mode == OutpostModeEnum.Write => code
                is RunLimit
                or AgentStopped
                or TransactionEnded
                or ResultTooLarge
                or ResultType,

            _ => false,
        };
    }

    // Answers the paragraph "The executor" of §8: true when the executor may hand the agent an outcome of this kind
    // with this code for a command of this mode. That is `succeeded`, with a null code; `failed` with `script-error`,
    // `result-type` or `result-too-large`; and, for a Write, `unknown` with `transaction-ended`.
    public static bool IsExecutorOutcome(OutpostModeEnum mode, OutpostAgentOutcomeKindEnum kind, string? code)
    {
        return kind switch
        {
            OutpostAgentOutcomeKindEnum.Succeeded => code is null,
            OutpostAgentOutcomeKindEnum.Failed => code is ScriptError or ResultType or ResultTooLarge,
            OutpostAgentOutcomeKindEnum.Unknown => mode == OutpostModeEnum.Write && code is TransactionEnded,
            _ => false,
        };
    }
}
