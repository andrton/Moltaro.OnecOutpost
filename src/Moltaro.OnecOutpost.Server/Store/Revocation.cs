namespace Moltaro.OnecOutpost.Server.Store;

// What a revocation gives: whether this call revoked the agent, and the commands it ended (rule 13), whose keys are set
// once it committed.
internal sealed record Revocation(bool Revoked, IReadOnlyList<Guid> Ended);
