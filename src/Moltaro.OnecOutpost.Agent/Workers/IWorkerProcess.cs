namespace Moltaro.OnecOutpost.Agent.Workers;

// One COM worker process, over its lines. It reports failures as results, by no exception but
// OperationCanceledException.
internal interface IWorkerProcess : IDisposable
{
    // Writes the connection line and gives the worker's ready.
    Task<bool> OpenAsync(WorkerConnectionLine connection, CancellationToken cancellationToken);

    // Writes the command line and gives the executor's size with the exact bytes of its outcome, or null when the
    // process ended or broke its line first.
    Task<(long Size, byte[] Outcome)?> RunAsync(WorkerCommandLine command, CancellationToken cancellationToken);

    // Ends the process and completes a pending RunAsync with null. It may run beside RunAsync, and is idempotent.
    void Kill();
}
