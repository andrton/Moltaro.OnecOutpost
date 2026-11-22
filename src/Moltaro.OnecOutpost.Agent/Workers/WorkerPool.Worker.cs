namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class WorkerPool
{
    // One worker of a base and mode: its process, the run limit of its mode, and what the pool knows of it. Who writes
    // what: the open's task sets the opened flag, once, from false to true; the loop's task sets IsHeld and the tasks;
    // the task of a run sets IsDropped while the command holds the worker, and the loop reads it only after Release,
    // which comes after it awaited that run.
    private sealed class Worker
    {
        private bool _opened;

        public Worker(IWorkerProcess process, TimeSpan runLimit)
        {
            Process = process;
            RunLimit = runLimit;
        }

        public IWorkerProcess Process { get; }

        public TimeSpan RunLimit { get; }

        // True once its open gave true within the deadline. Volatile, since the open's task writes it and the loop reads
        // it: a late read only under-declares free.
        public bool IsOpened => Volatile.Read(ref _opened);

        // A command holds it, from RunAsync to Release.
        public bool IsHeld { get; set; }

        // It ended, was killed, or gave base-unreachable or connection-lost: it runs nothing more.
        public bool IsDropped { get; set; }

        // The task of its open, and of its last run, which DisposeAsync waits for before it disposes the process.
        public Task Opening { get; set; } = Task.CompletedTask;

        public Task Running { get; set; } = Task.CompletedTask;

        public bool IsFree => IsOpened && !IsHeld && !IsDropped;

        public void Opened()
        {
            Volatile.Write(ref _opened, true);
        }

        // Ends the process, which runs nothing more. Kill is idempotent.
        public void Drop()
        {
            IsDropped = true;
            Process.Kill();
        }
    }
}
