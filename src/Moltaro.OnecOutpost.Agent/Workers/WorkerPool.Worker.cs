namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class WorkerPool
{
    // One worker of a base and mode: its process, the run limit of its mode, and what the pool knows of it. Who writes
    // what: the loop's task sets IsHeld and Running; the task of a run sets IsDropped while the command holds the
    // worker, and the loop reads it only after Release, which comes after it awaited that run; the end is completed
    // once, by the slot's open when the open did not give true, and otherwise by the run that dropped the worker.
    private sealed class Worker
    {
        // Its continuations run apart, so that the slot that waits for the end never runs inside the run that ended it.
        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Worker(IWorkerProcess process, TimeSpan runLimit)
        {
            Process = process;
            RunLimit = runLimit;
        }

        public IWorkerProcess Process { get; }

        public TimeSpan RunLimit { get; }

        // A command holds it, from RunAsync to Release.
        public bool IsHeld { get; set; }

        // Its open did not give true, it ended, was killed, or gave base-unreachable or connection-lost: it runs nothing
        // more.
        public bool IsDropped { get; set; }

        // The task of its last run, which its slot waits for on the pool's cancellation before it disposes the process.
        public Task Running { get; set; } = Task.CompletedTask;

        // Completes once nothing uses the process any more but its slot, which then disposes it.
        public Task Ended => _ended.Task;

        // Only a worker whose open gave true is published by its slot, so one that is published is open.
        public bool IsFree => !IsHeld && !IsDropped;

        // Ends the process, which runs nothing more. Kill is idempotent.
        public void Drop()
        {
            IsDropped = true;
            Process.Kill();
        }

        public void End()
        {
            _ended.TrySetResult();
        }
    }
}
