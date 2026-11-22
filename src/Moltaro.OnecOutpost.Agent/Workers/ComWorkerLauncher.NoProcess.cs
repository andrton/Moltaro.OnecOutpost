namespace Moltaro.OnecOutpost.Agent.Workers;

internal sealed partial class ComWorkerLauncher
{
    // What the launcher gives until S9b: no process. Its open gives false, so its mode never has a free worker, and a
    // run, which the pool never starts on it, gives null, as a process that ended does.
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

        public void Kill()
        {
        }

        public void Dispose()
        {
        }
    }
}
