using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Secrets;

// The secrets of the agent, as secrets.dat holds them: the agent's ECDSA P-256 key in its PKCS#8 form, the passwords
// of the 1C accounts by base key and mode, as the console's secret writes them, an empty password being a password, and
// the passwords of the worker accounts by base key, for the bases whose write has a workerAccount. A class, not a
// record, so that nothing prints a password. Limitation: a password stays in a string here for the life of the agent.
internal sealed class AgentSecrets
{
    public AgentSecrets(
        byte[] privateKey,
        IReadOnlyDictionary<(string Base, OutpostModeEnum Mode), string> passwords,
        IReadOnlyDictionary<string, string> workerPasswords)
    {
        PrivateKey = privateKey;
        Passwords = passwords;
        WorkerPasswords = workerPasswords;
    }

    public byte[] PrivateKey { get; }

    public IReadOnlyDictionary<(string Base, OutpostModeEnum Mode), string> Passwords { get; }

    // By base key.
    public IReadOnlyDictionary<string, string> WorkerPasswords { get; }
}
