namespace Moltaro.OnecOutpost.Server.Store;

// What the record of a poll gives: the state for its answer, and the commands that its list of received commands made
// Expired (rules 7, 11), whose keys are set once it committed. Expired is empty for an older poll.
internal sealed record PollRecord(PollState State, IReadOnlyList<Guid> Expired);
