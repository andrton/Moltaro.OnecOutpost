namespace Moltaro.OnecOutpost.Server.Store;

// A row of the table base_claims: the claim of one base key by the agent that holds it, keyed by the key and deleted with
// the agent (rule 14). A key is claimed by the first poll that declares it while no claim names it, and freed only by a
// release or a revocation.
internal sealed class BaseClaimRow
{
    public string Key { get; set; } = null!;

    public Guid AgentId { get; set; }
}
