using System.Diagnostics.CodeAnalysis;

namespace Moltaro.OnecOutpost.Protocol.Signing;

// The parts of the pairing token of §5 of docs/protocol.md, `outpost1.{agent}.{secret}.{fingerprint}`: the identity
// of the pending agent, the 16 random bytes of the secret, and the fingerprint of the server key as 64 lowercase
// hexadecimal digits (§2).
internal sealed record OutpostPairingTokenText(Guid Agent, byte[] Secret, string Fingerprint)
{
    private const string Prefix = "outpost1";

    private const int SecretLength = 16;

    private const int FingerprintLength = 64;

    // Writes the token in the form of §5.
    public string Write()
    {
        return $"{Prefix}.{OutpostForms.WriteId(Agent)}.{OutpostForms.WriteBase64Url(Secret)}.{Fingerprint}";
    }

    // Reads a token of §5. False, without an exception, for a text with another prefix, another number of parts, or a
    // part of another form: such a text is not a token.
    public static bool TryRead(string text, [NotNullWhen(true)] out OutpostPairingTokenText? token)
    {
        token = null;

        string[] parts = text.Split('.');

        if (parts.Length != 4 || parts[0] != Prefix)
        {
            return false;
        }

        if (!OutpostForms.TryReadId(parts[1], out Guid agent))
        {
            return false;
        }

        if (!OutpostForms.TryReadBase64Url(parts[2], out byte[]? secret) || secret.Length != SecretLength)
        {
            return false;
        }

        if (parts[3].Length != FingerprintLength || parts[3].AsSpan().ContainsAnyExcept("0123456789abcdef"))
        {
            return false;
        }

        token = new OutpostPairingTokenText(agent, secret, parts[3]);

        return true;
    }

    // Leaves the secret out, so that a log line or a message that shows the record never holds it.
    public override string ToString()
    {
        return $"{nameof(OutpostPairingTokenText)} {{ {nameof(Agent)} = {Agent}, {nameof(Fingerprint)} = {Fingerprint} }}";
    }
}
