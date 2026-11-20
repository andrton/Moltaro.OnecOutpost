namespace Moltaro.OnecOutpost.Agent.Policy;

// One base of the local policy: its key, how the agent connects to it, either Server and Database or File and never
// both, and its modes. Write is null when the base may not be written.
internal sealed record AgentBasePolicy(
    string Key,
    string? Server,
    string? Database,
    string? File,
    AgentModePolicy Read,
    AgentModePolicy? Write);
