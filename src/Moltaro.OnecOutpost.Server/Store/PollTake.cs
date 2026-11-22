using Moltaro.OnecOutpost.Protocol.Messages;

namespace Moltaro.OnecOutpost.Server.Store;

// What a take of a poll gives: the state for its answer, read in the take's transaction, and the commands it handed
// out, oldest first. No commands when the poll is no longer the newest of a Paired agent; the state then says why.
internal sealed record PollTake(PollState State, IReadOnlyList<OutpostSignedCommand> Commands);
