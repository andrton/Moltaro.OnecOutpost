namespace Moltaro.OnecOutpost.Agent.Transport;

// Why an answer to pair or to outcome was not accepted (§6.1 and §9 of docs/protocol.md), named once, for the console's
// one table of sentences.
internal enum ServerAnswerCauseEnum
{
    // No status came: no answer within the wait, or an error of the connection.
    Unreachable,

    // The unsigned body of §9 that the server uses for a protocol version it does not speak.
    ProtocolVersion,

    // pair: the server key of the answer does not match the fingerprint in the token.
    ServerKey,

    // A 200 whose signature does not verify.
    Signature,

    // outcome: a verified signed refusal, whose code the answer carries.
    Refusal,

    // A 401, 403 or 413 without an Outpost-Signature that verifies, whose meaning §9 gives; for pair, its 403, which
    // the server answers to any failure of pairing.
    Bare,

    // Anything else.
    Answer,
}
