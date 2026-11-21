namespace Moltaro.OnecOutpost.Protocol.Messages;

// What a poll declares for one mode of a base (§6.2 of docs/protocol.md): the number of its free workers and its run
// limit.
internal sealed record OutpostPollMode(int Free, TimeSpan RunLimit);
