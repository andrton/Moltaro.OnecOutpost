namespace Moltaro.OnecOutpost.Agent.Workers;

// One COM worker process, over its lines. Opens and runs report failures as results and throw only on cancellation.
internal interface IWorkerProcess : IDisposable
{
    // Writes the connection line and gives the worker's ready.
    Task<bool> OpenAsync(WorkerConnectionLine connection, CancellationToken cancellationToken);

    // Writes the command line and gives the executor's size with the exact bytes of its outcome, or null when the
    // process ended or broke its line first.
    Task<(long Size, byte[] Outcome)?> RunAsync(WorkerCommandLine command, CancellationToken cancellationToken);

    // Closes the input after the last open or run, waits for the process to exit within the deadline on the clock,
    // then terminates it and waits for its exit. A native wait failure throws Win32Exception. Kill may run beside it.
    // The caller disposes the process afterward.
    Task EndAsync(TimeSpan deadline, TimeProvider clock);

    // Ends the process, and completes a pending OpenAsync with false and a pending RunAsync with null. It may run beside
    // either and beside another Kill, from any thread, and is idempotent.
    void Kill();
}
