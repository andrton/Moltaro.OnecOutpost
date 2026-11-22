using Moltaro.OnecOutpost.Agent.Audit;

namespace Moltaro.OnecOutpost.Agent.Polls;

// The numbers of the polls of one identity (rule 10, §6.2 of docs/protocol.md), which grow across restarts through the
// reservations of the audit log. The run counts 1, 2, 3, … while its log holds no reservation of the identity, until
// an ordinary answer; otherwise a number comes from the clock's UTC ticks, above every number given and every newest
// answered. Not thread-safe: one task makes every change of the agent's files.
internal sealed class PollNumbers
{
    // The day of ticks that a reservation adds to the number it is recorded for.
    public const long ReservationSpan = 864_000_000_000;

    // Reads the highest reservation of the identity; one above 2⁶² throws AgentStopException with a reason that names
    // both causes, a cleared log that a first poll found and numbers used up.
    public PollNumbers(AuditLog log, Guid agent, TimeProvider clock)
    {
        throw new NotSupportedException();
    }

    // The number of the next poll. Before it gives a number at or above the below of the last reservation, or any
    // number while no reservation of the identity is known, it records a reservation with below that number plus
    // ReservationSpan; a number above 2⁶² is recorded so and throws AgentStopException.
    public long Next()
    {
        throw new NotSupportedException();
    }

    // Comes with every verified poll answer that is not lost: null for an ordinary answer, which ends counting, and the
    // newest of an answer to an older poll, above which the numbers after it are. Such an answer while counting is a
    // cleared log: it records a reservation with below 2⁶² + 1 and throws AgentStopException.
    public void Answered(long? newest)
    {
        throw new NotSupportedException();
    }
}
