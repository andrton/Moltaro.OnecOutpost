namespace Moltaro.OnecOutpost.Agent.Policy;

// One mode of a base, Read or Write: the 1C account it runs under, whose password is a secret the console writes, its
// workers and the run limit of each script.
internal sealed record AgentModePolicy(string Account, int Workers, TimeSpan RunLimit);
