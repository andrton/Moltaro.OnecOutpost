namespace Moltaro.OnecOutpost.Protocol.Messages;

// The body of a `pair` request (§6.1 of docs/protocol.md): the version of the agent, its new public key in the
// 91-byte form of §2, and the pairing proof of §3, 32 bytes.
internal sealed record OutpostPairRequest(string AgentVersion, byte[] Key, byte[] Proof);
