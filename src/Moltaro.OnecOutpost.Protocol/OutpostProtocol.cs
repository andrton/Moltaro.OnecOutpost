namespace Moltaro.OnecOutpost.Protocol;

// The fixed values of protocol v1 (docs/protocol.md): its version, the limits of §10 that both sides check alike, the
// ranges of §2 and the names of the headers of §4.
internal static class OutpostProtocol
{
    // The version that this build speaks. Every command carries it (§7), and every path of the endpoints names it as
    // v1 (§1).
    public const int Version = 1;

    // The nesting of §2 and §10: every message is read with this maximum depth.
    public const int MaxMessageDepth = 72;

    // The nesting of §2 and §10: the levels of parameters and of a result, counted from their own outer value.
    public const int MaxValueDepth = 64;

    // The body limits of §10, in bytes. The endpoints and the agent check them; the codec does not.
    public const int PairBodyLimit = 16 * 1024;

    public const int PollBodyLimit = 1024 * 1024;

    public const int PollAnswerLimit = 64 * 1024 * 1024;

    public const int AnswerLimit = 64 * 1024;

    // What the body limit of an outcome request adds to the outcome size limit (§10).
    public const int OutcomeEnvelopeAllowance = 4 * 1024;

    // The most bases that a poll declares (§6.2, §10).
    public const int MaxBases = 256;

    // The most entries of the list of received commands of a poll (§6.2, §10).
    public const int MaxReceived = 10_000;

    // The greatest poll number; the least is 1 (§6.2, §10).
    public const long MaxPollNumber = 1L << 62;

    // Ranges that the options, `policy.json` and the codec check alike (§2, "Ranges"). Every range includes its ends.
    // They are part of v1: widening one needs a new version of the protocol.
    public static readonly TimeSpan MinClockSkew = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromHours(1);

    public static readonly TimeSpan MinTakeTimeout = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan MaxTakeTimeout = TimeSpan.FromHours(1);

    public static readonly TimeSpan MinPollTimeout = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan MaxPollTimeout = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MinLeaseGrace = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan MaxLeaseGrace = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan MinRunLimit = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan MaxRunLimit = TimeSpan.FromDays(1);

    public const int MinOutcomeSizeLimit = 64 * 1024;

    public const int MaxOutcomeSizeLimit = 64 * 1024 * 1024;

    public const int MinCommandSizeLimit = 4 * 1024;

    public const int MaxCommandSizeLimit = 16 * 1024 * 1024;

    // The most workers of a mode, and so the greatest `free` of a mode in a poll (§6.2).
    public const int MaxWorkers = 8;

    // The headers of §4.
    public const string AgentHeader = "Outpost-Agent";

    public const string SignatureHeader = "Outpost-Signature";
}
