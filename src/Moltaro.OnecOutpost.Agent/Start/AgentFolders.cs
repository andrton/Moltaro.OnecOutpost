using Moltaro.OnecOutpost.Agent.Files;

namespace Moltaro.OnecOutpost.Agent.Start;

// The folders of one data folder: the root, which holds policy.json, agent.json and secrets.dat, audit\ and outcomes\.
internal sealed record AgentFolders(IAgentFolder Root, IAgentFolder Audit, IAgentFolder Outcomes);
