namespace Moltaro.OnecOutpost.Server.Store;

// A row of the table agent_bases: one base that the newest poll of an agent declared, keyed by the agent and the key and
// deleted with the agent. A poll that becomes the newest replaces the rows of its agent whole.
internal sealed class AgentBaseRow
{
    public Guid AgentId { get; set; }

    public string Key { get; set; } = null!;

    // Whether the local policy of the agent opens Write on the base. Every declared base opens Read in v1 (§6.2).
    public bool WriteOpen { get; set; }
}
