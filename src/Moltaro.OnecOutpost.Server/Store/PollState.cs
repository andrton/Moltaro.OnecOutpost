namespace Moltaro.OnecOutpost.Server.Store;

// What the store tells the answer to a poll. Revoked is true when the agent is not Paired. Newest is the number of the
// newest poll recorded when the poll in question is not it, and null when it is. Of the keys the newest poll declared,
// Held lists those whose claim names the agent and Claimed every other, both in ordinal key order; a key the agent holds
// and no longer declares is in neither.
internal sealed record PollState(bool Revoked, long? Newest, IReadOnlyList<string> Held, IReadOnlyList<string> Claimed);
