using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Server.Store;

// A row of the table commands: one Read or Write, from its creation to the end of its retention. Every change of a
// row is a conditional update, and every change that sets an outcome sets EndedAt.
internal sealed class CommandRow
{
    // The identity of the command, which the server generates and its bytes carry.
    public Guid Id { get; set; }

    // The order of insertion, an identity of the database, by which commands are handed out oldest first (rule 8).
    public long Sequence { get; set; }

    // The agent the command is addressed to, which held its base when it was created.
    public Guid AgentId { get; set; }

    public string BaseKey { get; set; } = null!;

    public OutpostModeEnum Mode { get; set; }

    // Null for a Read without a caller key.
    public string? CallerKey { get; set; }

    // The exact bytes of the command that were signed (§7 of docs/protocol.md), handed out as they are, and the
    // server's signature over them (§3). The script and the parameters are read from them where they are compared.
    public byte[] Bytes { get; set; } = null!;

    public byte[] Signature { get; set; } = null!;

    // The take deadline, the skew and the outcome limit that the command carries, as they were signed (§7).
    public DateTimeOffset Deadline { get; set; }

    public TimeSpan Skew { get; set; }

    public int OutcomeLimit { get; set; }

    public CommandPhaseEnum Phase { get; set; }

    // The run limit of the mode in the poll that last handed the command out; null while it is Waiting.
    public TimeSpan? RunLimit { get; set; }

    // The outcome: none while OutcomeKind is null; the agent's when Outcome holds its object as it came (§8); one the
    // server set when Outcome is null. Code is the code of OutpostCodes, or null.
    public OutpostOutcomeKindEnum? OutcomeKind { get; set; }

    public string? OutcomeCode { get; set; }

    public byte[]? Outcome { get; set; }

    // Retention starts when the outcome is set or replaced, as timed by the store's TimeProvider.
    public DateTimeOffset? EndedAt { get; set; }
}
