using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The boundary of the COM workers (principle 8): it logs on the worker account of a base and starts the worker process
// of one base and mode. ComWorkerLauncher implements it, and a fake in tests.
internal interface IWorkerLauncher
{
    // Logs on the local Windows account that runs every COM worker of the base, Reads included, before any Start: the
    // start calls it once for each base with a workerAccount, in the policy's order. A failure is an
    // AgentStopException whose reason never holds the password.
    void LogOn(string baseKey, string account, string password);

    IWorkerProcess Start(string baseKey, OutpostModeEnum mode);
}
