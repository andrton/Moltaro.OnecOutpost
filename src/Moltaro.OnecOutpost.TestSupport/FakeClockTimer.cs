namespace Moltaro.OnecOutpost.TestSupport;

// A timer of a FakeClock: the timer of FakeTimeProvider, each change of which is reported to the clock once it is made.
internal sealed class FakeClockTimer(FakeClock clock, ITimer timer) : ITimer
{
    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        bool changed = timer.Change(dueTime, period);

        clock.Report(this, dueTime, period);

        return changed;
    }

    public void Dispose()
    {
        timer.Dispose();
        clock.Forget(this);
    }

    public async ValueTask DisposeAsync()
    {
        await timer.DisposeAsync();
        clock.Forget(this);
    }
}
