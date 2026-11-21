using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Protocol.Tests.Outcomes;

// The tables of §8 of docs/protocol.md, written out as the document gives them: the outcomes an agent may send, by
// mode, and the outcomes the executor may hand the agent. The first has the codes of the connection to 1C, which S9
// adds to it: Failed base-unreachable for a Read and a Write, Failed connection-lost for a Read and Unknown
// connection-lost for a Write. The tests of the outcome codes and of the reader of the outcome object both go through
// every combination of a mode, a kind and a code, and expect what these tables say.
internal static class OutcomeTable
{
    public static readonly string[] Modes = ["read", "write"];

    public static readonly string[] Kinds = ["succeeded", "failed", "refused", "unknown"];

    // No code, every code of the contract, and a text that is no code.
    public static readonly string?[] Codes =
    [
        null,
        "script-error",
        "result-type",
        "result-too-large",
        "run-limit",
        "lost",
        "caller-key-conflict",
        "unknown-base",
        "mode-closed",
        "not-served",
        "not-addressed",
        "protocol-version",
        "malformed",
        "expired",
        "clock-skew",
        "revoked",
        "disconnected",
        "agent-stopped",
        "transaction-ended",
        "base-unreachable",
        "connection-lost",
        "timeout",
    ];

    private static readonly string[] FailedOfRead =
    [
        "script-error",
        "result-type",
        "result-too-large",
        "run-limit",
        "lost",
        "base-unreachable",
        "connection-lost",
    ];

    private static readonly string[] FailedOfWrite = ["script-error", "result-type", "result-too-large", "base-unreachable"];

    private static readonly string[] RefusedOfEitherMode =
    [
        "not-served",
        "not-addressed",
        "protocol-version",
        "malformed",
        "expired",
        "clock-skew",
    ];

    private static readonly string[] UnknownOfWrite =
    [
        "run-limit",
        "agent-stopped",
        "transaction-ended",
        "result-too-large",
        "result-type",
        "connection-lost",
    ];

    private static readonly string[] FailedOfTheExecutor = ["script-error", "result-type", "result-too-large"];

    // The codes of a failure of the connection to 1C, which the COM worker reports (S9).
    private static readonly string[] CodesOfTheConnectionTo1C = ["base-unreachable", "connection-lost"];

    // Every combination of a mode, a kind and a code.
    public static TheoryData<string, string, string?> Combinations => CombinationsWhere((_, _, _) => true);

    // The outcomes with a code of the connection to 1C that an agent may send: Failed base-unreachable for a Read and a
    // Write, Failed connection-lost for a Read and Unknown connection-lost for a Write. The tests read them apart from
    // the other combinations, since the codec accepts them only from S9 on; every other combination with these codes
    // is refused already.
    public static TheoryData<string, string, string?> AgentOutcomesOfTheConnectionTo1C =>
        CombinationsWhere(IsAgentOutcomeOfTheConnectionTo1C);

    public static TheoryData<string, string, string?> CombinationsWithoutTheAgentOutcomesOfTheConnectionTo1C =>
        CombinationsWhere((mode, kind, code) => !IsAgentOutcomeOfTheConnectionTo1C(mode, kind, code));

    // Whether an agent may send the outcome for the mode, or for either mode when the mode is null, and its code is one
    // of the connection to 1C.
    public static bool IsAgentOutcomeOfTheConnectionTo1C(string? mode, string kind, string? code)
    {
        bool allowed = mode is null
            ? AgentMaySend("read", kind, code) || AgentMaySend("write", kind, code)
            : AgentMaySend(mode, kind, code);

        return allowed && CodesOfTheConnectionTo1C.Contains(code);
    }

    // The table "The outcomes an agent may send, by mode".
    public static bool AgentMaySend(string mode, string kind, string? code)
    {
        return (kind, mode) switch
        {
            ("succeeded", _) => code is null,
            ("failed", "read") => FailedOfRead.Contains(code),
            ("failed", "write") => FailedOfWrite.Contains(code),
            ("refused", _) => RefusedOfEitherMode.Contains(code),
            ("unknown", "write") => UnknownOfWrite.Contains(code),
            _ => false,
        };
    }

    // The paragraph "The executor".
    public static bool ExecutorMayHandOver(string mode, string kind, string? code)
    {
        return (kind, mode) switch
        {
            ("succeeded", _) => code is null,
            ("failed", _) => FailedOfTheExecutor.Contains(code),
            ("unknown", "write") => code == "transaction-ended",
            _ => false,
        };
    }

    public static OutpostModeEnum Mode(string mode)
    {
        return mode switch
        {
            "read" => OutpostModeEnum.Read,
            "write" => OutpostModeEnum.Write,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    public static OutpostAgentOutcomeKindEnum Kind(string kind)
    {
        return kind switch
        {
            "succeeded" => OutpostAgentOutcomeKindEnum.Succeeded,
            "failed" => OutpostAgentOutcomeKindEnum.Failed,
            "refused" => OutpostAgentOutcomeKindEnum.Refused,
            "unknown" => OutpostAgentOutcomeKindEnum.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static TheoryData<string, string, string?> CombinationsWhere(Func<string, string, string?, bool> include)
    {
        TheoryData<string, string, string?> combinations = [];

        foreach (string mode in Modes)
        {
            foreach (string kind in Kinds)
            {
                foreach (string? code in Codes)
                {
                    if (include(mode, kind, code))
                    {
                        combinations.Add(mode, kind, code);
                    }
                }
            }
        }

        return combinations;
    }
}
