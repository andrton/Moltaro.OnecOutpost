namespace Moltaro.OnecOutpost.Agent.Secrets;

// The form of secrets.dat: Protect over {"key":…,"passwords":[{"base":…,"mode":"read","password":…},…]}, written by
// the writer of §2 of docs/protocol.md with the key in base64url.
internal static class SecretsFile
{
    // Unprotects the file and reads its secrets. The CryptographicException of the protection passes; content that
    // breaks the form, a base key against its rule or a base and mode given twice is FormatException, quoting nothing.
    public static AgentSecrets Read(byte[] file, IMachineProtection protection)
    {
        throw new NotSupportedException();
    }

    // Writes the secrets in the form of the file and protects them.
    public static byte[] Write(AgentSecrets secrets, IMachineProtection protection)
    {
        throw new NotSupportedException();
    }
}
