using Microsoft.Extensions.Time.Testing;

namespace Moltaro.OnecOutpost.TestSupport;

// The fake clock of the tests: a FakeTimeProvider, whose time moves only when a test advances it, wrapped so that every
// timer the code under test creates, changes or disposes is reported to the clock. A test waits until the code under
// test has armed its timer before it advances the time; advanced earlier, the time would pass before the timer is
// armed, and the timer would fire late. A test also waits until a timer has ended, disposed or changed to fire never,
// when the code under test ends it right after something the test cannot see otherwise, such as the end of a race.
public sealed class FakeClock : TimeProvider
{
    private readonly FakeTimeProvider _time;

    // Guards _timers, _waits, _ended and _endWaits. FakeTimeProvider is never called under it.
    private readonly Lock _lock = new();

    // Every timer of this clock as it was last armed: when it was due first, and its period.
    private readonly Dictionary<ITimer, (DateTimeOffset DueAt, TimeSpan Period)> _timers = [];

    // The waits of WaitForTimersAsync that not enough armed timers have completed yet.
    private readonly List<(DateTimeOffset DueBy, int Count, TaskCompletionSource Armed)> _waits = [];

    // The times that the timers which ended, disposed or changed to fire never, were last armed to fire first at.
    private readonly List<DateTimeOffset> _ended = [];

    // The waits of WaitForTimerEndedAsync that no ended timer has completed yet.
    private readonly List<(DateTimeOffset DueAt, TaskCompletionSource Ended)> _endWaits = [];

    // A clock at the start time of FakeTimeProvider.
    public FakeClock()
    {
        _time = new FakeTimeProvider();
    }

    // A clock that starts at the given time. A restart with the clock set back is a new start with a clock of its own.
    public FakeClock(DateTimeOffset start)
    {
        _time = new FakeTimeProvider(start);
    }

    public override long TimestampFrequency => _time.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => _time.LocalTimeZone;

    public override DateTimeOffset GetUtcNow() => _time.GetUtcNow();

    public override long GetTimestamp() => _time.GetTimestamp();

    // Moves the time forward. FakeTimeProvider fires every timer that becomes due on the calling thread before this
    // returns.
    public void Advance(TimeSpan delta) => _time.Advance(delta);

    // Completes once a timer of this clock is armed to fire at or before dueBy, at once if one already is.
    public Task WaitForTimerAsync(DateTimeOffset dueBy, CancellationToken cancellationToken)
    {
        return WaitForTimersAsync(dueBy, 1, cancellationToken);
    }

    // Completes once at least `count` timers of this clock are armed to fire at or before dueBy, at once if they
    // already are: for code under test that arms one timer for each of several waits at once.
    public Task WaitForTimersAsync(DateTimeOffset dueBy, int count, CancellationToken cancellationToken)
    {
        DateTimeOffset now = GetUtcNow();

        // Asynchronous continuations, so that the test never goes on inside the call of the code that armed the timer.
        TaskCompletionSource armed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            if (CountTimersDueBy(dueBy, now) >= count)
            {
                return Task.CompletedTask;
            }

            _waits.Add((dueBy, count, armed));
        }

        return armed.Task.WaitAsync(cancellationToken);
    }

    // Completes once a timer of this clock that was armed to fire first at dueAt has ended, disposed or changed to fire
    // never, at once if one has, whether it fired before or not.
    public Task WaitForTimerEndedAsync(DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        // Asynchronous continuations, so that the test never goes on inside the call of the code that ended the timer.
        TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            if (_ended.Contains(dueAt))
            {
                return Task.CompletedTask;
            }

            _endWaits.Add((dueAt, ended));
        }

        return ended.Task.WaitAsync(cancellationToken);
    }

    // Whether a timer of this clock is armed to fire at or before dueBy, now. With WaitForTimerAsync, a test states when
    // the code under test armed its timer to fire: armed by a moment, and not by the moment before.
    public bool HasTimerDueBy(DateTimeOffset dueBy)
    {
        DateTimeOffset now = GetUtcNow();

        lock (_lock)
        {
            return CountTimersDueBy(dueBy, now) > 0;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        FakeClockTimer timer = new(this, _time.CreateTimer(callback, state, dueTime, period));

        Report(timer, dueTime, period);

        return timer;
    }

    // Records a timer as armed. Called only after the timer of FakeTimeProvider is armed, so that a test that this
    // completes can advance the time at once.
    internal void Report(ITimer timer, TimeSpan dueTime, TimeSpan period)
    {
        DateTimeOffset now = GetUtcNow();

        lock (_lock)
        {
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                End(timer);
            }
            else
            {
                _timers[timer] = (now + dueTime, period);
            }

            for (int index = _waits.Count - 1; index >= 0; index--)
            {
                if (CountTimersDueBy(_waits[index].DueBy, now) >= _waits[index].Count)
                {
                    _waits[index].Armed.SetResult();
                    _waits.RemoveAt(index);
                }
            }
        }
    }

    internal void Forget(ITimer timer)
    {
        lock (_lock)
        {
            End(timer);
        }
    }

    // Records the end of a timer that was armed, under the lock, and completes the waits for it.
    private void End(ITimer timer)
    {
        if (!_timers.Remove(timer, out (DateTimeOffset DueAt, TimeSpan Period) armed))
        {
            return;
        }

        _ended.Add(armed.DueAt);

        for (int index = _endWaits.Count - 1; index >= 0; index--)
        {
            if (_endWaits[index].DueAt == armed.DueAt)
            {
                _endWaits[index].Ended.SetResult();
                _endWaits.RemoveAt(index);
            }
        }
    }

    private int CountTimersDueBy(DateTimeOffset dueBy, DateTimeOffset now)
    {
        int count = 0;

        foreach ((DateTimeOffset dueAt, TimeSpan period) in _timers.Values)
        {
            if (NextFiring(dueAt, period, now) <= dueBy)
            {
                count++;
            }
        }

        return count;
    }

    // FakeTimeProvider fires a timer as soon as the time reaches it, even inside the call that arms it with a due time
    // of zero. A timer due at or before now has therefore fired: a one-shot timer fires no more, and a periodic one
    // next fires at the first multiple of its period after now.
    private static DateTimeOffset? NextFiring(DateTimeOffset dueAt, TimeSpan period, DateTimeOffset now)
    {
        if (dueAt > now)
        {
            return dueAt;
        }

        if (period == TimeSpan.Zero || period == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        long periodsPassed = (now - dueAt).Ticks / period.Ticks;

        return dueAt + (period * (periodsPassed + 1));
    }
}
