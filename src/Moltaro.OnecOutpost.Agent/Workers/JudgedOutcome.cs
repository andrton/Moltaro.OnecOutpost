namespace Moltaro.OnecOutpost.Agent.Workers;

// What WorkerOutcome.Judge makes of a worker's output: the bytes of the outcome to record and send, and whether the
// worker is to be dropped.
internal sealed record JudgedOutcome(byte[] Outcome, bool EndsWorker);
