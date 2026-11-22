using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The boundary of the COM workers (principle 8): it starts the worker process of one base and mode. The COM worker of
// S9 implements it, and a fake in tests.
internal interface IWorkerLauncher
{
    IWorkerProcess Start(string baseKey, OutpostModeEnum mode);
}
