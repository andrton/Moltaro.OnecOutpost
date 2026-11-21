namespace Moltaro.OnecOutpost.Protocol.Messages;

// The body of the answer `200` to `pair` (§6.1 of docs/protocol.md): the server's time, the identity and the key that
// the agent sent, the server's public key, which the agent pins, and the server's options PollTimeout and LeaseGrace,
// which bound the first poll. Both keys are in the 91-byte form of §2.
internal sealed record OutpostPairAnswer(
    DateTimeOffset Time,
    Guid Agent,
    byte[] Key,
    byte[] ServerKey,
    TimeSpan PollTimeout,
    TimeSpan LeaseGrace);
