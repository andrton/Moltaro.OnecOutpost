namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class ComWorkerLauncher
{
    // The process of a start that found no connector registered, or whose program did not start: none. Its open gives
    // false, so its slot tries again after its delay, and a run, which the pool never starts on it, gives null, as a
    // process that ended does.
    private sealed class NoProcess : IWorkerProcess
    {
        public Task<bool> OpenAsync(WorkerConnectionLine connection, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<(long Size, byte[] Outcome)?> RunAsync(WorkerCommandLine command, CancellationToken cancellationToken)
        {
            return Task.FromResult<(long Size, byte[] Outcome)?>(null);
        }

        public Task EndAsync(TimeSpan deadline, TimeProvider clock)
        {
            return Task.CompletedTask;
        }

        public void Kill()
        {
        }

        public void Dispose()
        {
        }
    }
}
