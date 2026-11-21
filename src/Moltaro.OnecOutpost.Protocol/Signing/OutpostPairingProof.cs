namespace Moltaro.OnecOutpost.Protocol.Signing;

// The pairing proof of §3 of docs/protocol.md: an HMAC-SHA256, keyed by the secret of the token, over
// `outpost/1 pair-proof {agent} {keyHash}`, where `{keyHash}` is SHA-256 over the 91 bytes of the agent's key as 64
// lowercase hexadecimal digits. It binds the agent's key to the secret without sending the secret. `agentKey` is the
// key in the form of §2.
internal static class OutpostPairingProof
{
    // Computes the proof: 32 bytes.
    public static byte[] Compute(ReadOnlySpan<byte> secret, Guid agent, ReadOnlySpan<byte> agentKey)
    {
        throw new NotSupportedException();
    }

    // True when `proof` is the proof of the secret, the identity and the key. It compares in constant time
    // (CryptographicOperations.FixedTimeEquals), and gives false, without an exception, for a proof of another length.
    public static bool Check(ReadOnlySpan<byte> secret, Guid agent, ReadOnlySpan<byte> agentKey, ReadOnlySpan<byte> proof)
    {
        throw new NotSupportedException();
    }
}
