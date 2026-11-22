namespace Moltaro.OnecOutpost.Server.Tests.Pairing;

// What fails in a pairing that the server refuses (§6.1 of docs/protocol.md).
public enum PairFailureEnum
{
    // The token paired an agent already; another key comes with it.
    SecondUse,

    // The clock of the node stands at the expiry of the token.
    ExpiredToken,

    // The proof was computed with another secret than the token's.
    WrongProof,

    // Something on the path put a key of its own in place of the agent's, and signed with it.
    SwappedKey,

    // The request is not signed with the key it carries.
    WrongSignature,

    // The body names a member twice, which §2 refuses.
    BrokenBody,
}
