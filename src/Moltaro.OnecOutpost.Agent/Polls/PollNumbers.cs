using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Protocol;

namespace Moltaro.OnecOutpost.Agent.Polls;

// The numbers of the polls of one identity (rule 10, §6.2 of docs/protocol.md), which grow across restarts through the
// reservations of the audit log. The run counts 1, 2, 3, … while its log holds no reservation of the identity, until
// an ordinary answer; otherwise a number comes from the clock's UTC ticks, above every number given and every newest
// answered. Not thread-safe: one task makes every change of the agent's files.
internal sealed class PollNumbers
{
    // The day of ticks that a reservation adds to the number it is recorded for.
    public const long ReservationSpan = 864_000_000_000;

    // Both causes of a reservation above 2⁶², for the reason of a start that finds one.
    private const string NoNumberLeft =
        "the audit log holds a reservation that leaves no poll number: either a first poll found that the log had been cleared, or the poll numbers are used up; pair the agent anew";

    private readonly AuditLog _log;

    private readonly Guid _agent;

    private readonly TimeProvider _clock;

    // The below of the last reservation of the identity, which bounds every number from above while it runs; null while
    // none is known.
    private long? _bound;

    // True while the log held no reservation of the identity when the run started and no ordinary answer came yet.
    private bool _counting;

    // The last number given, 0 before the first.
    private long _last;

    // The greatest newest of an answer to an older poll, 0 before the first.
    private long _newest;

    // Reads the highest reservation of the identity; one above 2⁶² throws AgentStopException with a reason that names
    // both causes, a cleared log that a first poll found and numbers used up.
    public PollNumbers(AuditLog log, Guid agent, TimeProvider clock)
    {
        _log = log;
        _agent = agent;
        _clock = clock;
        _bound = log.HighestReservation(agent);
        _counting = _bound is null;

        if (_bound > OutpostProtocol.MaxPollNumber)
        {
            throw new AgentStopException(NoNumberLeft);
        }
    }

    // The number of the next poll. Before it gives a number at or above the below of the last reservation, or any
    // number while no reservation of the identity is known, it records a reservation with below that number plus
    // ReservationSpan; a number above 2⁶² is recorded so and throws AgentStopException.
    public long Next()
    {
        long number = _counting ? _last + 1 : FromClock();

        // No number above 2⁶² is given, whatever the bound, which a reservation near 2⁶² puts above it. Such a number is
        // recorded as one at or above the bound, the record of this first stop (§6.2), and stops the agent.
        bool passesGreatest = number > OutpostProtocol.MaxPollNumber;

        // The reservation is on the disk before the number is given, so that a number sent after a restart is above
        // every number sent before it, however the clock was moved meanwhile.
        if (_bound is not long bound || number >= bound || passesGreatest)
        {
            _log.Reservation(_agent, number + ReservationSpan);
            _bound = number + ReservationSpan;

            if (passesGreatest)
            {
                throw new AgentStopException("the poll numbers are used up; pair the agent anew");
            }
        }

        _last = number;

        return number;
    }

    // Comes with every verified poll answer that is not lost: null for an ordinary answer, which ends counting, and the
    // newest of an answer to an older poll, above which the numbers after it are. Such an answer while counting is a
    // cleared log: it records a reservation with below 2⁶² + 1 and throws AgentStopException.
    public void Answered(long? newest)
    {
        if (newest is not long older)
        {
            _counting = false;

            return;
        }

        if (_counting)
        {
            // One poll is open at a time and the last number given was a counted one, so a server that answers it as
            // older has recorded polls of this identity that the log does not hold: the log was cleared, and with it
            // the commands it had received. The reservation leaves no number, so that nothing runs until a new pairing.
            _log.Reservation(_agent, OutpostProtocol.MaxPollNumber + 1);

            throw new AgentStopException(
                "the server has recorded polls of this agent that its audit log does not hold: the log was cleared; pair the agent anew");
        }

        _newest = Math.Max(_newest, older);
    }

    // The greatest of the clock's UTC ticks, one above the last number, one above the greatest newest answered, and,
    // for the first number of the run, the highest reservation.
    private long FromClock()
    {
        long number = Math.Max(_clock.GetUtcNow().UtcTicks, Math.Max(_last, _newest) + 1);

        if (_last == 0 && _bound is long highest)
        {
            number = Math.Max(number, highest);
        }

        return number;
    }
}
