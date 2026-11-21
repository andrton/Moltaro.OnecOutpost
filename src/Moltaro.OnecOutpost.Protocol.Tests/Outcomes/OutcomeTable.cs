using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Protocol.Tests.Outcomes;

// The tables of §8 of docs/protocol.md, written out as the document gives them: the outcomes an agent may send, by
// mode, and the outcomes the executor may hand the agent. The tests of the outcome codes and of the reader of the
// outcome object both go through every combination of a mode, a kind and a code, and expect what these tables say.
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
        "timeout",
    ];

    private static readonly string[] FailedOfRead = ["script-error", "result-type", "result-too-large", "run-limit", "lost"];

    private static readonly string[] FailedOfWrite = ["script-error", "result-type", "result-too-large"];

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
    ];

    private static readonly string[] FailedOfTheExecutor = ["script-error", "result-type", "result-too-large"];

    // Every combination of a mode, a kind and a code.
    public static TheoryData<string, string, string?> Combinations
    {
        get
        {
            TheoryData<string, string, string?> combinations = [];

            foreach (string mode in Modes)
            {
                foreach (string kind in Kinds)
                {
                    foreach (string? code in Codes)
                    {
                        combinations.Add(mode, kind, code);
                    }
                }
            }

            return combinations;
        }
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
}
