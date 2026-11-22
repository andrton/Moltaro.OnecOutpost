namespace Moltaro.OnecOutpost.Agent.Transport;

// What the answer to an outcome request gives the loop (§9 of docs/protocol.md).
internal enum OutcomeResultEnum
{
    // A verified 200 whose confirmed is the request's command.
    Confirmed,

    // A verified result-too-large, or a 413 without an Outpost-Signature that verifies, whatever its body.
    TooLarge,

    // Any other answer, or none.
    Lost,
}
