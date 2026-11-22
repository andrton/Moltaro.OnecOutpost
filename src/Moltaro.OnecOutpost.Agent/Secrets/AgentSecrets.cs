using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Secrets;

// The secrets of the agent, as secrets.dat holds them: the agent's ECDSA P-256 key in its PKCS#8 form, and the
// passwords of the 1C accounts by base key and mode, as the console's secret writes them, an empty password being a
// password. A class, not a record, so that nothing prints a password.
internal sealed class AgentSecrets
{
    public AgentSecrets(byte[] privateKey, IReadOnlyDictionary<(string Base, OutpostModeEnum Mode), string> passwords)
    {
        PrivateKey = privateKey;
        Passwords = passwords;
    }

    public byte[] PrivateKey { get; }

    public IReadOnlyDictionary<(string Base, OutpostModeEnum Mode), string> Passwords { get; }
}
