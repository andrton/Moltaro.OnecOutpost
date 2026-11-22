namespace Moltaro.OnecOutpost.Agent.Workers;

// What a run gives the loop: the bytes of the outcome to record and send, and the clock's time from the call to the
// outcome.
internal sealed record WorkerRun(byte[] Outcome, long DurationMs);
