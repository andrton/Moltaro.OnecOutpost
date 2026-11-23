namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class WorkerPool
{
    // One slot of a mode, which holds one worker at a time: the worker that the slot's task published once its open
    // gave true, or null. The slot's task writes it with Volatile.Write, and Free and RunAsync, on the loop's task, read
    // it with Volatile.Read: a worker read late is one not yet opened, so free is only under-declared.
    private sealed class Slot
    {
        private Worker? _worker;

        public Worker? Worker => Volatile.Read(ref _worker);

        public void Publish(Worker? worker)
        {
            Volatile.Write(ref _worker, worker);
        }
    }
}
