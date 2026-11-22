using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Agent.Pairing;

// The pair request (§6.1 of docs/protocol.md), which the console of S10 calls (principle 12: the console depends on the
// agent). It writes no file: the console writes agent.json and secrets.dat.
internal static class AgentPairing
{
    // Makes a P-256 key, sends pair with the proof and reads the answer with ReadPairAnswer, waiting requestTimeout
    // over the answer and its body, read within 64 KiB. Gives Settings and the key in PKCS#8 for a verified 200;
    // otherwise the status when one came, and Unreachable without one, Refused for a bare 403, ProtocolVersion for the
    // unsigned body of §9, ServerKey and Signature for the codec's codes, Answer for anything else.
    public static Task<AgentPairingResult> PairAsync(
        Uri server,
        OutpostPairingTokenText token,
        string agentVersion,
        HttpMessageHandler handler,
        TimeSpan requestTimeout,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
