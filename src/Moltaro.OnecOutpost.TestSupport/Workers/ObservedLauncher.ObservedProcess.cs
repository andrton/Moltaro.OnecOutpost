using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.TestSupport.Workers;

internal sealed partial class ObservedLauncher
{
    // One worker of the wrapped launcher, whose open and runs it reports to its launcher.
    private sealed class ObservedProcess : IWorkerProcess
    {
        private readonly ObservedLauncher _launcher;

        private readonly IWorkerProcess _process;

        private readonly string _base;

        private readonly OutpostModeEnum _mode;

        private readonly int _number;

        public ObservedProcess(ObservedLauncher launcher, IWorkerProcess process, string baseKey, OutpostModeEnum mode, int number)
        {
            _launcher = launcher;
            _process = process;
            _base = baseKey;
            _mode = mode;
            _number = number;
        }

        public async Task<bool> OpenAsync(WorkerConnectionLine connection, CancellationToken cancellationToken)
        {
            bool ready = false;

            try
            {
                ready = await _process.OpenAsync(connection, cancellationToken);

                return ready;
            }
            finally
            {
                _launcher.ReportOpened(_base, _mode, _number, ready);
            }
        }

        public Task<(long Size, byte[] Outcome)?> RunAsync(WorkerCommandLine command, CancellationToken cancellationToken)
        {
            Task<(long Size, byte[] Outcome)?> run = _process.RunAsync(command, cancellationToken);

            _launcher.ReportRunBegan(_base, _mode, command);

            return run;
        }

        public void Kill()
        {
            _process.Kill();
        }

        public Task EndAsync(TimeSpan deadline, TimeProvider clock)
        {
            return _process.EndAsync(deadline, clock);
        }

        public void Dispose()
        {
            _process.Dispose();
        }
    }
}
