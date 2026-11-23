namespace Moltaro.OnecOutpost.Agent.Policy;

// One mode of a base, Read or Write: the 1C account it runs under, whose password is a secret the console writes, its
// workers, the run limit of each script, and whether its scripts run in safe mode. SafeMode is always true for Read;
// for Write it is false only when the policy turns safe mode off. WorkerAccount is the local Windows account that runs
// all the COM workers of the base, write.workerAccount of the policy, given exactly when the write turns safe mode off;
// null for every Read and for a Write in safe mode.
internal sealed record AgentModePolicy(string Account, int Workers, TimeSpan RunLimit, bool SafeMode, string? WorkerAccount);
