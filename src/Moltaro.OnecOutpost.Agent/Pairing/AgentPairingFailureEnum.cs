namespace Moltaro.OnecOutpost.Agent.Pairing;

// Why a pairing ended without a verified 200 (§6.1 and §9 of docs/protocol.md), for the console of S10 to report in its
// own words.
internal enum AgentPairingFailureEnum
{
    // No status came: no answer within the wait, or an error of the connection.
    Unreachable,

    // A bare 403: the server answers so to any failure of pairing, and so can anything on the path.
    Refused,

    // The unsigned body of §9 that the server uses for a protocol version it does not speak.
    ProtocolVersion,

    // The codec's server-key: the server key of the answer does not match the fingerprint in the token.
    ServerKey,

    // The codec's signature: the answer's signature does not verify.
    Signature,

    // Anything else.
    Answer,
}
