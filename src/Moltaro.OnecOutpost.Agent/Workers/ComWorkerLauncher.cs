using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The program's launcher of the COM workers, which S9b implements. Until then it starts no process, and the OpenAsync
// of what it starts gives false, so every mode declares free 0.
internal sealed partial class ComWorkerLauncher : IWorkerLauncher
{
    public IWorkerProcess Start(string baseKey, OutpostModeEnum mode)
    {
        return new NoProcess();
    }
}
