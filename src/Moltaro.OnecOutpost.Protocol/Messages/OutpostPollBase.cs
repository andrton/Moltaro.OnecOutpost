namespace Moltaro.OnecOutpost.Protocol.Messages;

// One base that a poll declares (§6.2 of docs/protocol.md): its key and its modes. Write is null when the local
// policy does not open writing, and is then written as `"write":null`.
internal sealed record OutpostPollBase(string Key, OutpostPollMode Read, OutpostPollMode? Write);
