using Moltaro.OnecOutpost.Agent.Transport;

namespace Moltaro.OnecOutpost.Agent.Pairing;

// What a pairing gives the console: for a verified 200, the settings that agent.json holds and the agent's new key in
// its PKCS#8 form; otherwise the HTTP status when one came, and the failure. A class, not a record: it holds the
// private key, and nothing prints it.
internal sealed class AgentPairingResult
{
    public AgentPairingSettings? Settings { get; init; }

    public byte[]? PrivateKey { get; init; }

    public int? Status { get; init; }

    public ServerAnswerCauseEnum? Failure { get; init; }
}
